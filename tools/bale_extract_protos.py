#!/usr/bin/env python3
"""Recover .proto definitions from a minified ts-proto / grpc-web JavaScript bundle.

Target: the Bale web client (rspack/webpack bundle, ts-proto generated codecs).
Standard library only.  Nothing is executed and no network access is used.

Usage:
    python3 tools/bale_extract_protos.py BUNDLE_OR_DIR OUT_DIR [--methods-md FILE] [--json FILE]

BUNDLE_OR_DIR   the app bundle (index.*.js) or a directory of bundles.  When a
                directory is given every *.js file is scanned and module ids are
                treated as one global namespace (as webpack does).
OUT_DIR         receives one .proto per gRPC service, plus websocket.proto,
                common.proto (messages reachable from more than one file) and
                unscoped.proto (codecs not reachable from any service/websocket root).

How it works
  1. The bundle is split into webpack modules with a small JS-aware brace scanner
     (strings, template literals, regex literals, comments are skipped).
  2. Inside each module every `NAME={encode(...){...},decode(...){...}}` object is a
     message codec.  Field numbers, wire tags, names and types come from the `case N:`
     branches of decode(); the defaults function (`function F(){return{...}}`) tells
     which scalars are `optional`; encode() tells whether a repeated scalar is packed.
  3. Per-module scoping: the same single-letter identifiers are reused by every
     module, so identifiers are resolved (module id, local name).  Cross-module
     references go through `var x=a(ID)` imports and `a.d(o,{Export:()=>local})`
     export maps, including re-exports.
  4. gRPC method descriptors (`{methodName:"X",service:..,requestType:{..encode(this)..},
     responseType:{..decode(r)..}}`) give request/response messages and readable names.
  5. Self-check prints counts of messages, services, methods and unresolved references.
"""
import argparse
import glob
import json
import os
import re
import sys

# ----------------------------------------------------------------------------
# JavaScript-aware scanning helpers
# ----------------------------------------------------------------------------

KW_BEFORE_REGEX = {'return', 'typeof', 'case', 'in', 'of', 'void', 'delete', 'throw',
                   'new', 'else', 'do', 'instanceof'}
IDENT = r'[A-Za-z_$][\w$]*'


def skip_string(s, i):
    q = s[i]
    i += 1
    while s[i] != q:
        if s[i] == '\\':
            i += 1
        i += 1
    return i + 1


def skip_template(s, i):
    i += 1
    while s[i] != '`':
        if s[i] == '\\':
            i += 2
            continue
        if s[i] == '$' and s[i + 1] == '{':
            i = skip_balanced(s, i + 1)
            continue
        i += 1
    return i + 1


def skip_regex(s, i):
    i += 1
    incls = False
    while True:
        c = s[i]
        if c == '\\':
            i += 2
            continue
        if c == '[':
            incls = True
        elif c == ']':
            incls = False
        elif c == '/' and not incls:
            break
        i += 1
    i += 1
    while s[i].isalpha():
        i += 1
    return i


def skip_balanced(s, i):
    """s[i] is an opening bracket; return the index just after its matching close."""
    depth = 0
    n = len(s)
    prev = ''
    while i < n:
        c = s[i]
        if c in '"\'':
            i = skip_string(s, i)
            prev = '"'
            continue
        if c == '`':
            i = skip_template(s, i)
            prev = '"'
            continue
        if c == '/':
            nx = s[i + 1]
            if nx == '/':
                i = s.index('\n', i)
                continue
            if nx == '*':
                i = s.index('*/', i) + 2
                continue
            isre = prev == '' or prev in '(,=:[!&|?{};+-*%<>~^'
            if not isre and prev == 'w':
                m = re.search(r'([A-Za-z_$][\w$]*)$', s[max(0, i - 12):i].rstrip())
                isre = bool(m and m.group(1) in KW_BEFORE_REGEX)
            if isre:
                i = skip_regex(s, i)
                prev = '"'
                continue
            prev = '/'
            i += 1
            continue
        if c in '([{':
            depth += 1
        elif c in ')]}':
            depth -= 1
            if depth == 0:
                return i + 1
        if c.isspace():
            i += 1
            continue
        prev = 'w' if (c.isalnum() or c in '_$') else c
        i += 1
    raise ValueError('unbalanced bracket')


def split_top(text, sep=','):
    """Split text on a separator char at nesting depth 0."""
    out, i, start, n = [], 0, 0, len(text)
    while i < n:
        c = text[i]
        if c in '"\'':
            i = skip_string(text, i)
        elif c == '`':
            i = skip_template(text, i)
        elif c in '([{':
            i = skip_balanced(text, i)
        elif c == sep:
            out.append(text[start:i])
            i += 1
            start = i
        else:
            i += 1
    out.append(text[start:])
    return out


def split_modules(s, brace):
    """s[brace] is the '{' of a webpack module map -> {id: (params, body)}."""
    end = skip_balanced(s, brace)
    i = brace + 1
    mods = {}
    key_re = re.compile(r'\s*,?\s*(\d+(?:e\d+)?|[A-Za-z_$][\w$]*)')
    arrow_re = re.compile(r'function\s*\(([^)]*)\)\s*\{|\(([^)]*)\)\s*=>\s*\{|(\w+)\s*=>\s*\{')
    while i < end - 1:
        m = key_re.match(s, i)
        if not m:
            raise ValueError('bad module key at %d' % i)
        key = m.group(1)
        i = m.end()
        if re.fullmatch(r'\d+e\d+', key):
            key = str(int(float(key)))
        if s[i] == ':':
            i += 1
            m2 = arrow_re.match(s, i)
            params = m2.group(1) or m2.group(2) or m2.group(3) or ''
            i = m2.end() - 1
        else:
            j = s.index(')', i)
            params = s[i + 1:j]
            i = j + 1
        e = skip_balanced(s, i)
        mods[key] = (params, s[i + 1:e - 1])
        i = e
    return mods


def load_bundle_modules(path):
    s = open(path, encoding='utf-8', errors='replace').read()
    m = re.search(r'__webpack_modules__=\{|\.push\(\[\[[\d,]+\],\{', s)
    if not m:
        return {}
    return split_modules(s, m.end() - 1)


# ----------------------------------------------------------------------------
# Data model
# ----------------------------------------------------------------------------

SCALARS = {'int32', 'uint32', 'sint32', 'int64', 'uint64', 'sint64', 'fixed32', 'fixed64',
           'sfixed32', 'sfixed64', 'bool', 'string', 'bytes', 'double', 'float'}
WIRE_OF = {'int32': 0, 'uint32': 0, 'sint32': 0, 'int64': 0, 'uint64': 0, 'sint64': 0,
           'bool': 0, 'fixed64': 1, 'sfixed64': 1, 'double': 1, 'fixed32': 5,
           'sfixed32': 5, 'float': 5, 'string': 2, 'bytes': 2}
NUMERIC = SCALARS - {'string', 'bytes'}


class Field:
    def __init__(self, num, name):
        self.num = num
        self.name = name
        self.scalar = None      # scalar type name
        self.ref = None         # raw JS expression of message codec
        self.target = None      # resolved codec key (mid, name)
        self.unwrap = False     # .decode(..).value  => google wrapper type
        self.repeated = False
        self.packed = False
        self.optional = False
        self.map = None         # (entry_ref_expr, entry_target)
        self.tags = []
        self.int64_js = None    # 'string' | 'number' | None (informational)


class Codec:
    def __init__(self, mid, name):
        self.mid = mid
        self.name = name
        self.fields = []
        self.defaults = {}
        self.is_map_entry = False
        self.wrapper_type = None   # e.g. google.protobuf.StringValue
        self.problems = []
        self.order = 0

    @property
    def key(self):
        return (self.mid, self.name)


class Module:
    def __init__(self, mid, params, body):
        self.mid = mid
        self.body = body
        ps = [p.strip() for p in params.split(',')] if params.strip() else []
        self.exp_name = ps[1] if len(ps) > 1 else None
        self.req_name = ps[2] if len(ps) > 2 else None
        self.imports = {}   # local var -> module id
        self.exports = {}   # export name -> JS expression
        self.codecs = {}    # local name -> Codec
        self.services = {}  # local var -> service name
        self.methods = []   # method descriptor dicts
        self.enums = []     # (var, [(name, value)])


# ----------------------------------------------------------------------------
# Module analysis
# ----------------------------------------------------------------------------

def parse_imports_exports(mod):
    b, req, exp = mod.body, mod.req_name, mod.exp_name
    if not req:
        return
    for m in re.finditer(r'(?<![\w$.])(' + IDENT + r')=' + re.escape(req) + r'\((\d+)\)', b):
        mod.imports[m.group(1)] = m.group(2)
    if exp:
        for m in re.finditer(re.escape(req) + r'\.d\(' + re.escape(exp) + r',\{', b):
            brace = m.end() - 1
            end = skip_balanced(b, brace)
            objs = [(b[brace + 1:end - 1], True)]
            if b.startswith(',{', end):      # three-argument form: d(exports, {}, {name: local})
                end2 = skip_balanced(b, end + 1)
                objs.append((b[end + 2:end2 - 1], False))
            for txt, getter in objs:
                for ent in split_top(txt):
                    pat = r'\s*(' + IDENT + r'|\d+):' + (r'\(\)=>(.+)$' if getter else r'(.+)$')
                    mm = re.match(pat, ent, re.S)
                    if mm:
                        mod.exports[mm.group(1)] = mm.group(2).strip()


def parse_object_top(text):
    """'{a:1,b:"x"}' -> {'a': '1', 'b': '"x"'} (shallow)."""
    text = text.strip()
    assert text[0] == '{'
    out = {}
    for ent in split_top(text[1:-1]):
        if ':' in ent:
            k, v = ent.split(':', 1)
            out[k.strip()] = v.strip()
    return out


READER_TYPES = '|'.join(sorted(SCALARS, key=len, reverse=True))
RE_REF = r'((?:\(0,)?[\w$]+(?:\.[\w$]+)*\)?|' + IDENT + r'\(\d+\)(?:\.[\w$]+)+)'


def parse_case(cod, num, text, encode_txt):
    """Fill a Field from the text of one `case N:` branch of decode()."""
    f = Field(num, None)
    t = text.strip()
    tags = [int(x) for x in re.findall(r'(?<![\w$.])(\d+)(?:!==|===)[\w$]+', t)]
    f.tags = tags
    # map entry
    m = re.match(r'\{if\((\d+)!==\w+\)break;let (\w+)=(' + r'[\w$.]+' + r')\.decode\(\w+,\w+\.uint32\(\)\);'
                 r'void 0!==\2\.value&&\(\w+\.([\w$]+)\[\2\.key\]=\2\.value\)', t)
    if m:
        f.name = m.group(4)
        f.map = (m.group(3), None)
        f.tags = [int(m.group(1))]
        return f
    # packed repeated: if(T===r){x.push(...);continue}if(T2!==r)break;...
    m = re.match(r'if\((\d+)===\w+\)\{\w+\.([\w$]+)\.push\((.*?)\);\s*(?:continue;?)?\}', t, re.S)
    if m:
        f.name = m.group(2)
        f.repeated = True
        f.packed = True
        mm = re.search(r'\.(' + READER_TYPES + r')\(\)', m.group(3))
        f.scalar = mm.group(1) if mm else None
        if f.scalar is None:
            cod.problems.append('packed-unknown:%s' % m.group(2))
        return f
    m = re.match(r'if\((\d+)!==\w+\)break;(.*)$', t, re.S)
    if not m:
        cod.problems.append('case-unparsed:%d' % num)
        return None
    stmt = m.group(2)
    f.tags = [int(m.group(1))]
    ms = re.match(r'\w+\.([\w$]+)(\.push\()?(=)?', stmt)
    if not ms:
        cod.problems.append('stmt-unparsed:%d' % num)
        return None
    f.name = ms.group(1)
    f.repeated = bool(ms.group(2))
    val = stmt[ms.end():]
    # scalar reads
    mm = re.match(r'(?:[\w$]+\()?\w+\.(' + READER_TYPES + r')\(\)(\.toString\(\))?', val)
    if mm and not re.match(r'[\w$.]+\.decode', val):
        f.scalar = mm.group(1)
        if f.scalar in ('int64', 'uint64', 'sint64', 'fixed64', 'sfixed64'):
            if mm.group(2):
                f.int64_js = 'string'
            elif val.startswith(('(', )) or re.match(r'[\w$]+\(', val):
                f.int64_js = 'number'
        return f
    mm = re.match(r'(' + r'[\w$]+(?:\.[\w$]+)*|' + IDENT + r'\(\d+\)(?:\.[\w$]+)+)\.decode\(\w+,\w+\.uint32\(\)\)(\.value)?', val)
    if mm:
        f.ref = mm.group(1)
        f.unwrap = bool(mm.group(2))
        return f
    cod.problems.append('value-unparsed:%d:%s' % (num, val[:40]))
    return None


def parse_codec(mod, name, obj_start, order):
    b = mod.body
    end = skip_balanced(b, obj_start)
    obj = b[obj_start:end]
    cod = Codec(mod.mid, name)
    cod.order = order
    # encode / decode texts
    ie = obj.find('encode(')
    idc = obj.find('decode(')
    enc = obj[ie:idc] if 0 <= ie < idc else ''
    dec = obj[idc:]
    # defaults function
    md = re.search(r'=(' + IDENT + r')\(\);for\(;', dec)
    if md:
        mf = re.search(r'function ' + re.escape(md.group(1)) + r'\(\)\{return(\{)', b)
        if mf:
            cod.defaults = parse_object_top(b[mf.end() - 1:skip_balanced(b, mf.end() - 1)])
    # cases
    ms = re.search(r'switch\(\w+>>>3\)\{', dec)
    fields = []
    if not ms:
        # single-field form used for packed repeated scalars: if(r>>>3==N){if(T===r){..}if(T2===r){..}}
        for mi in re.finditer(r'if\(\w+>>>3==(\d+)\)\{', dec):
            bo = mi.end() - 1
            be = skip_balanced(dec, bo)
            f = parse_case(cod, int(mi.group(1)), dec[bo + 1:be - 1], enc)
            if f is not None:
                f.num = int(mi.group(1))
                fields.append(f)
    if ms:
        sw_open = ms.end() - 1
        sw_end = skip_balanced(dec, sw_open)
        body = dec[sw_open + 1:sw_end - 1]
        parts = re.split(r'(?:(?<=;)|(?<=\})|^)case (\d+):', body)
        # parts: ['', num, text, num, text...]
        for k in range(1, len(parts), 2):
            num = int(parts[k])
            text = parts[k + 1]
            text = re.sub(r'\s*continue;?\s*$', '', text)
            text = re.sub(r'(?:;\s*continue;?)(?=\s*$)', '', text)
            f = parse_case(cod, num, text, enc)
            if f is not None:
                f.num = num
                fields.append(f)
    # encode check: packed or not, tags consistent
    enc_tags = set(int(x) for x in re.findall(r'uint32\((\d+)\)', enc))
    dec_tags = set(t for f in fields for t in f.tags)
    for f in fields:
        if f.packed and f.scalar:
            ptag = (f.num << 3) | 2
            f.packed = ptag in enc_tags
            # packed encode uses uint32(ptag).fork(); unpacked uses the element tag
            if not re.search(r'uint32\(%d\)\.fork\(\)' % ptag, enc):
                f.packed = False
        elif f.repeated and f.scalar in NUMERIC:
            # decode saw only the unpacked branch
            f.packed = False
    # packed repeated numeric fields must be listed with both tags in decode; keep tags
    for f in fields:
        dv = cod.defaults.get(f.name)
        if (not f.repeated and f.map is None and f.scalar is not None and dv is not None
                and dv.replace(' ', '') == 'void0'):
            f.optional = True
    stray = [t for t in enc_tags if t not in dec_tags and not any(
        (t >> 3) == f.num for f in fields)]
    if stray:
        cod.problems.append('encode-tags-not-in-decode:%s' % sorted(stray))
    cod.fields = fields
    return cod, end


def parse_module(mid, params, body):
    mod = Module(mid, params, body)
    parse_imports_exports(mod)
    order = 0
    for m in re.finditer(r'(?<![\w$.])(' + IDENT + r')=\{encode\(', body):
        name = m.group(1)
        try:
            cod, _ = parse_codec(mod, name, m.end() - len('{encode('), order)
        except Exception as e:  # keep going, report in self-check
            cod = Codec(mid, name)
            cod.problems.append('exception:%r' % (e,))
        mod.codecs[name] = cod
        order += 1
    # TypeScript enums: (s=d||(d={}))[s.NAME=0]="NAME",s[s.OTHER=1]="OTHER"
    for m in re.finditer(r'\((' + IDENT + r')=(' + IDENT + r')\|\|\(\2=\{\}\)\)', body):
        alias, var = m.group(1), m.group(2)
        pos = m.end()
        vals = []
        first = re.compile(r'\[' + re.escape(alias) + r'\.(' + IDENT + r')=(-?\d+)\]="(\w+)"')
        nxt = re.compile(r',' + re.escape(alias) + r'\[' + re.escape(alias) + r'\.(' + IDENT + r')=(-?\d+)\]="(\w+)"')
        mm = first.match(body, pos)
        while mm:
            vals.append((mm.group(3), int(mm.group(2))))
            pos = mm.end()
            mm = nxt.match(body, pos)
        if vals:
            mod.enums.append((var, vals))
    # services and method descriptors
    for m in re.finditer(r'(?<![\w$.])(' + IDENT + r')=\{serviceName:"([\w.]+)"\}', body):
        mod.services[m.group(1)] = m.group(2)
    for m in re.finditer(r'(?<![\w$.])(' + IDENT + r')=\{methodName:"(\w+)",service:(' + IDENT + r'|\{serviceName:"[\w.]+"\}),'
                         r'requestStream:!([01]),responseStream:!([01]),', body):
        start = m.start() + len(m.group(1)) + 1
        end = skip_balanced(body, start)
        txt = body[start:end]
        rq = re.search(r'serializeBinary\(\)\{return ([\w$.]+(?:\(\d+\)[\w$.]*)?)\.encode\(this\)', txt)
        rs = re.search(r'deserializeBinary\(\w+\)\{let \w+=([\w$.]+(?:\(\d+\)[\w$.]*)?)\.decode\(\w+\)', txt)
        mod.methods.append({
            'var': m.group(1), 'method': m.group(2), 'service_var': m.group(3),
            'client_stream': m.group(4) == '0', 'server_stream': m.group(5) == '0',
            'req': rq.group(1) if rq else None, 'res': rs.group(1) if rs else None,
            'pos': m.start()})
    return mod


# ----------------------------------------------------------------------------
# Whole-program model
# ----------------------------------------------------------------------------

class Program:
    def __init__(self):
        self.modules = {}
        self.unresolved = []   # (where, expr)

    def load(self, paths):
        for p in paths:
            for mid, (params, body) in load_bundle_modules(p).items():
                if mid in self.modules:
                    continue
                if '{encode(' in body or 'methodName:"' in body:
                    self.modules[mid] = parse_module(mid, params, body)
                else:
                    self.modules[mid] = Module(mid, params, body)

    # --- reference resolution -------------------------------------------------
    def resolve(self, mid, expr, depth=0):
        """Resolve a JS expression used inside module `mid` to a codec key or None."""
        if depth > 12 or expr is None:
            return None
        expr = expr.strip()
        expr = re.sub(r'^\(0,(.*)\)$', r'\1', expr)
        mod = self.modules.get(mid)
        if mod is None:
            return None
        m = re.fullmatch(r'(' + IDENT + r')', expr)
        if m:
            if expr in mod.codecs:
                return mod.codecs[expr].key
            return None
        m = re.fullmatch(r'(' + IDENT + r')\.(' + IDENT + r'|\d+)', expr)
        if m and m.group(1) in mod.imports:
            return self.resolve_export(mod.imports[m.group(1)], m.group(2), depth + 1)
        m = re.fullmatch(IDENT + r'\((\d+)\)\.(' + IDENT + r')', expr)
        if m:
            return self.resolve_export(m.group(1), m.group(2), depth + 1)
        return None

    def resolve_export(self, mid, name, depth):
        mod = self.modules.get(mid)
        if mod is None or depth > 12:
            return None
        e = mod.exports.get(name)
        if e is None:
            return None
        return self.resolve(mid, e, depth + 1)

    def codec(self, key):
        return self.modules[key[0]].codecs[key[1]]

    def all_codecs(self):
        out = []
        for mid in sorted(self.modules, key=lambda x: int(x) if x.isdigit() else 0):
            out.extend(sorted(self.modules[mid].codecs.values(), key=lambda c: c.order))
        return out

    def resolve_all(self):
        for cod in self.all_codecs():
            for f in cod.fields:
                if f.ref:
                    f.target = self.resolve(cod.mid, f.ref)
                    if f.target is None:
                        self.unresolved.append(('%s.%s' % (self.label(cod), f.name), f.ref))
                if f.map:
                    tgt = self.resolve(cod.mid, f.map[0])
                    f.map = (f.map[0], tgt)
                    if tgt is None:
                        self.unresolved.append(('%s.%s(map)' % (self.label(cod), f.name), f.map[0]))
        # map entries & wrappers
        for cod in self.all_codecs():
            for f in cod.fields:
                if f.map and f.map[1]:
                    self.codec(f.map[1]).is_map_entry = True
        for cod in self.all_codecs():
            for f in cod.fields:
                if f.target and f.unwrap:
                    w = self.codec(f.target)
                    vf = [x for x in w.fields if x.num == 1]
                    if len(w.fields) == 1 and vf and vf[0].name == 'value' and vf[0].scalar:
                        w.wrapper_type = WRAPPERS.get(vf[0].scalar)
                    else:
                        w.problems.append('unwrap-of-non-wrapper')

    def fold_shape_wrappers(self, role_keys):
        """Single-field `value = 1` scalar messages that are not RPC request/response types are
        wire-identical to google.protobuf.*Value; render them as such."""
        for cod in self.all_codecs():
            if (len(cod.fields) == 1 and cod.fields[0].name == 'value' and cod.fields[0].num == 1
                    and cod.fields[0].scalar in WRAPPERS and cod.wrapper_type is None
                    and not cod.fields[0].repeated and not cod.fields[0].optional
                    and cod.key not in role_keys):
                cod.wrapper_type = WRAPPERS[cod.fields[0].scalar]

    def label(self, cod):
        return 'm%s.%s' % (cod.mid, cod.name)

    # --- services ----------------------------------------------------------------
    def collect_services(self):
        services = {}   # full name -> list of method dicts (ordered)
        for mid, mod in self.modules.items():
            for md in mod.methods:
                sv = md['service_var']
                inline = re.match(r'\{serviceName:"([\w.]+)"\}', sv)
                full = inline.group(1) if inline else mod.services.get(sv)
                md['service'] = full
                md['mid'] = mid
                md['req_key'] = self.resolve(mid, md['req'])
                md['res_key'] = self.resolve(mid, md['res'])
                if md['req_key'] is None:
                    self.unresolved.append(('%s/%s request' % (full, md['method']), md['req']))
                if md['res_key'] is None:
                    self.unresolved.append(('%s/%s response' % (full, md['method']), md['res']))
                services.setdefault(full, []).append(md)
        for full in services:
            services[full].sort(key=lambda d: (d['mid'], d['pos']))
        return services


WRAPPERS = {'string': 'StringValue', 'int32': 'Int32Value', 'int64': 'Int64Value',
            'uint32': 'UInt32Value', 'uint64': 'UInt64Value', 'bool': 'BoolValue',
            'bytes': 'BytesValue', 'double': 'DoubleValue', 'float': 'FloatValue'}


# ----------------------------------------------------------------------------
# Naming and file layout
# ----------------------------------------------------------------------------

def camel(s):
    return s[:1].upper() + s[1:]


def children(prog, cod):
    out = []
    for f in cod.fields:
        if f.target:
            out.append(f.target)
        if f.map and f.map[1]:
            ec = prog.codec(f.map[1])
            for g in ec.fields:
                if g.target:
                    out.append(g.target)
    return out


def reach(prog, roots):
    seen = set()
    stack = [r for r in roots if r]
    while stack:
        k = stack.pop()
        if k in seen:
            continue
        seen.add(k)
        stack.extend(children(prog, prog.codec(k)))
    return seen


def assign_names(prog, services, ws_roots):
    names = {}
    used = set()
    aliases = {}
    inferred = set()

    def take(base, key):
        n = base
        i = 2
        while n in used:
            n = '%s%d' % (base, i)
            i += 1
        used.add(n)
        names[key] = n
        return n

    # role names from gRPC methods
    slots = {}
    for full, mds in services.items():
        for md in mds:
            for role, k in (('Request', md['req_key']), ('Response', md['res_key'])):
                if k:
                    slots.setdefault(k, []).append((full, md['method'], role))
    for k, sl in slots.items():
        cod = prog.codec(k)
        if cod.is_map_entry or cod.wrapper_type:
            continue
        first = sl[0]
        if not cod.fields and len(sl) > 1:
            take('Empty' if 'Empty' not in used else 'Empty_m%s_%s' % (k[0], k[1]), k)
        else:
            take(first[1] + first[2], k)
        aliases[k] = sl
    # websocket roots
    for label, k in ws_roots.items():
        if k not in names:
            take(label, k)
    all_c = prog.all_codecs()
    # shape-based friendly names (inferred)
    for cod in all_c:
        if cod.key in names or cod.is_map_entry or cod.wrapper_type:
            continue
        sn = signature_name(cod)
        if sn:
            take(sn, cod.key)
            inferred.add(cod.key)
    # derive from a unique (parent, field) reference, repeatedly
    refs = {}
    for cod in all_c:
        for f in cod.fields:
            tg = []
            if f.target and not f.unwrap:
                tg.append(f.target)
            if f.map and f.map[1]:
                ec = prog.codec(f.map[1])
                for g in ec.fields:
                    if g.target and not g.unwrap:
                        tg.append(g.target)
            for t in tg:
                refs.setdefault(t, set()).add((cod.key, f.name))
    changed = True
    while changed:
        changed = False
        for cod in all_c:
            k = cod.key
            if k in names or cod.is_map_entry or cod.wrapper_type:
                continue
            r = refs.get(k, set())
            if len(r) == 1:
                pk, fname = next(iter(r))
                if pk in names and pk != k:
                    take('%s_%s' % (names[pk], camel(fname)), k)
                    changed = True
    for cod in all_c:
        if cod.key not in names and not cod.is_map_entry and not cod.wrapper_type:
            take('Msg_%s_%s' % (cod.mid, cod.name.replace('$', '_S')), cod.key)
    assign_names.inferred = inferred
    return names, aliases


# Friendly names for widely shared messages, matched on the leading field names.  These are
# INFERRED from the shape of the message (the real names are minified away).
SIGNATURE_NAMES = [
    (('type', 'id', 'accessHash'), 'ExPeer'),
    (('type', 'id'), 'Peer'),
    (('uid', 'accessHash'), 'UserOutPeer'),
    (('exPeerType', 'peerIdentity', 'peerCategory'), 'ExPeerInfo'),
    (('fileId', 'accessHash', 'fileStorageVersion'), 'FileLocation'),
    (('smallImage', 'largeImage', 'fullImage', 'id', 'date'), 'Avatar'),
    (('fileLocation', 'width', 'height', 'fileSize'), 'ImageLocation'),
    (('id', 'accessHash', 'name', 'localName', 'sex', 'avatar', 'isBot'), 'User'),
    (('id', 'accessHash', 'title', 'avatar', 'ownerUid', 'createDate', 'groupType'), 'Group'),
    (('id', 'theme', 'about', 'ownerUid', 'createDate'), 'FullGroup'),
    (('uid', 'inviterUid', 'date', 'isAdmin'), 'Member'),
    (('stringValue', 'booleanValue', 'int32Value', 'int64Value', 'doubleValue', 'arrayValue'), 'Value'),
    (('items',), 'MetadataItems'),
    (('bankMessage', 'binaryMessage', 'deletedMessage', 'documentMessage'), 'MessageContent'),
    (('senderUid', 'rid', 'date', 'message', 'state', 'reactions'), 'HistoryMessage'),
    (('peer', 'unreadCount', 'sortDate', 'senderUid', 'rid', 'date', 'message'), 'Dialog'),
    (('text', 'mentions', 'ext', 'messageTag'), 'TextMessage'),
    (('seq', 'date', 'state', 'ext', 'seqBig'), 'SeqState'),
    (('date', 'rid', 'seq'), 'MessageId'),
    (('fileId', 'url', 'timeout', 'unsignedUrl'), 'FileUrl'),
    (('offset', 'limit', 'hash'), 'FileHashBlock'),
    (('url', 'secureUrl', 'type', 'height', 'width'), 'OpenGraphImage'),
    (('type', 'url', 'title', 'description', 'images'), 'OpenGraphObject'),
]


def signature_name(cod):
    fn = tuple(f.name for f in cod.fields)
    best = None
    for sig, nm in SIGNATURE_NAMES:
        if fn[:len(sig)] == sig and (best is None or len(sig) > len(best[0])):
            best = (sig, nm)
    return best[1] if best else None


def find_ws_roots(prog):
    roots = {}
    sig = {
        'ClientPack': {'request', 'ping', 'handshakeRequest'},
        'ServerPack': {'response', 'update', 'terminateSession', 'pong', 'handshakeResponse'},
    }
    for cod in prog.all_codecs():
        fn = {f.name for f in cod.fields}
        for label, req in sig.items():
            if req <= fn and label not in roots:
                roots[label] = cod.key
    if 'ClientPack' in roots:
        mid = roots['ClientPack'][0]
        for cod in prog.modules[mid].codecs.values():
            if [f.name for f in cod.fields] == ['code', 'message', 'details']:
                roots['ApiError'] = cod.key     # decoded from ServerPack.response.error
    return roots


def type_name(prog, f, names, file_of, cur_file, pkg_of, key=None):
    """Proto type string for a field (scalar or message)."""
    if f.scalar:
        return f.scalar
    tk = key if key else f.target
    if tk is None:
        return 'bytes'
    tc = prog.codec(tk)
    if tc.wrapper_type:
        return 'google.protobuf.' + tc.wrapper_type
    n = names[tk]
    tf = file_of[tk]
    if tf == cur_file:
        return n
    return '.%s.%s' % (pkg_of[tf], n)


def render(prog, services, names, aliases, ws_roots, outdir):
    # group assignment
    groups = {}
    for full, mds in services.items():
        groups[full] = reach(prog, [m['req_key'] for m in mds] + [m['res_key'] for m in mds])
    groups['websocket'] = reach(prog, list(ws_roots.values()))
    covered = set().union(*groups.values()) if groups else set()
    unscoped_roots = [c.key for c in prog.all_codecs()
                      if c.key not in covered and not c.is_map_entry and not c.wrapper_type]
    groups['unscoped'] = reach(prog, unscoped_roots)
    owners = {}
    for g, ks in groups.items():
        for k in ks:
            owners.setdefault(k, set()).add(g)

    def fname_of_group(g):
        if g in ('websocket', 'unscoped'):
            return g + '.proto'
        return g.replace('.', '_') + '.proto'

    file_of, pkg_of = {}, {}
    for k, gs in owners.items():
        if len(gs) == 1:
            g = next(iter(gs))
            file_of[k] = fname_of_group(g)
        else:
            file_of[k] = 'common.proto'

    def pkg_for(fn):
        if fn == 'common.proto':
            return 'bale.shared'
        if fn == 'websocket.proto':
            return 'bale.ws'
        if fn == 'unscoped.proto':
            return 'bale.unscoped'
        for full in services:
            if fname_of_group(full) == fn:
                return full.rsplit('.', 1)[0]
        return 'bale.misc'

    files = sorted(set(file_of.values()) | {fname_of_group(s) for s in services})
    for fn in files:
        pkg_of[fn] = pkg_for(fn)
    # two services in one package would clash on message names; names are globally unique anyway.

    out_files = {}
    for fn in files:
        keys = [k for k, f in file_of.items() if f == fn]
        keys.sort(key=lambda k: (int(k[0]) if k[0].isdigit() else 0, prog.codec(k).order))
        keys = [k for k in keys if not prog.codec(k).is_map_entry and not prog.codec(k).wrapper_type]
        lines = ['// Generated by tools/bale_extract_protos.py from a minified JS bundle.',
                 '// Field names/numbers/wire types are recovered from ts-proto codecs;',
                 '// message names are derived (see comments) because the originals are minified.',
                 'syntax = "proto3";', '', 'package %s;' % pkg_of[fn], '']
        imports = set()
        body = []
        for full, mds in services.items():
            if fname_of_group(full) != fn:
                continue
            body.append('service %s {' % full.rsplit('.', 1)[1])
            for md in mds:
                def tn(k):
                    c = prog.codec(k)
                    t = names[k]
                    if file_of[k] != fn:
                        imports.add(file_of[k])
                        t = '.%s.%s' % (pkg_of[file_of[k]], t)
                    return t
                body.append('  rpc %s(%s%s) returns (%s%s);' % (
                    md['method'], 'stream ' if md['client_stream'] else '', tn(md['req_key']),
                    'stream ' if md['server_stream'] else '', tn(md['res_key'])))
            body.append('}')
            body.append('')
        for k in keys:
            cod = prog.codec(k)
            n = names[k]
            cm = '// js module %s, local %s' % (cod.mid, cod.name)
            if k in aliases:
                cm += '; used as ' + ', '.join('%s/%s %s' % (a, b, c) for a, b, c in aliases[k][:6])
            elif k in assign_names.inferred:
                cm += '; name inferred from field shape; fields: ' + ', '.join(f.name for f in cod.fields[:12])
            elif n.startswith('Msg_'):
                cm += '; fields: ' + (', '.join(f.name for f in cod.fields) or '(none)')
            body.append(cm)
            body.append('message %s {' % n)
            for f in cod.fields:
                body.extend(render_field(prog, f, names, file_of, fn, pkg_of, imports))
            body.append('}')
            body.append('')
        if any(prog.codec(k).wrapper_type is None and False for k in keys):
            pass
        text_body = '\n'.join(body)
        if 'google.protobuf.' in text_body:
            imports.add('google/protobuf/wrappers.proto')
        for im in sorted(imports):
            lines.append('import "%s";' % im)
        if imports:
            lines.append('')
        out_files[fn] = '\n'.join(lines + body) + '\n'
    enum_txt = render_enums(prog)
    if enum_txt:
        out_files['enums.proto'] = enum_txt
    os.makedirs(outdir, exist_ok=True)
    for old in glob.glob(os.path.join(outdir, '*.proto')):       # drop stale files from earlier runs
        try:
            if open(old).read(200).startswith('// Generated by tools/bale_extract_protos.py'):
                os.remove(old)
        except OSError:
            pass
    for fn, txt in out_files.items():
        with open(os.path.join(outdir, fn), 'w') as fh:
            fh.write(txt)
    return out_files, file_of, pkg_of, groups


def enum_name(vals):
    names_ = [v[0] for v in vals]
    pre = os.path.commonprefix(names_)
    if '_' in pre:
        pre = pre[:pre.rindex('_') + 1]
    else:
        pre = ''
    return pre, (pre.rstrip('_') or 'ENUM')


def collect_enums(prog):
    out = []
    for mid in sorted(prog.modules, key=lambda x: int(x) if x.isdigit() else 0):
        for var, vals in prog.modules[mid].enums:
            pre, nm = enum_name(vals)
            out.append((mid, var, nm, vals))
    return out


def enum_hint(prog, f):
    """Guess which TS enum an int32 field uses, from names only (INFERRED, may be wrong)."""
    if f.scalar != 'int32' or f.map:
        return None
    ix = getattr(prog, '_enum_index', None)
    if ix is None:
        ix = []
        seen = {}
        for mid, var, nm, vals in collect_enums(prog):
            base = ''.join(p.capitalize() for p in nm.split('_')) or 'Enum'
            seen[base] = seen.get(base, 0) + 1
            ix.append((nm.replace('_', '').lower(), base if seen[base] == 1 else '%s%d' % (base, seen[base])))
        prog._enum_index = ix
    fl = f.name.lower()
    if len(fl) < 4:
        return None
    cands = [n for k, n in ix if k != 'appid' and (k == fl or (len(k) > 5 and fl.endswith(k)) or (len(fl) >= 6 and k.endswith(fl)))]
    cands = sorted(set(cands))
    return cands[0] if len(cands) == 1 else None


def render_enums(prog):
    enums = collect_enums(prog)
    if not enums:
        return ''
    lines = ['// Generated by tools/bale_extract_protos.py.',
             '// Enums recovered from TypeScript enum initialisers.  The wire codecs decode',
             '// enum fields with a plain int32 read, so the link field -> enum is NOT in the',
             '// bundle; fields in the other files stay int32.  Names come from the common',
             '// prefix of the value names (the original enum names are minified away).',
             'syntax = "proto3";', '', 'package bale.enums;', '']
    seen_vals = set()
    seen_names = set()
    for mid, var, nm, vals in enums:
        base = ''.join(p.capitalize() for p in nm.split('_')) or 'Enum'
        n = base
        i = 2
        while n in seen_names:
            n = '%s%d' % (base, i)
            i += 1
        seen_names.add(n)
        lines.append('// js module %s, local %s' % (mid, var))
        lines.append('enum %s {' % n)
        has_zero = any(v == 0 for _, v in vals)
        if not has_zero:
            lines.append('  %s_UNSPECIFIED_ = 0;' % nm.upper())
        dup = False
        for vn, vv in vals:
            if vn in seen_vals:
                vn = '%s_%s' % (n.upper(), vn)
            seen_vals.add(vn)
            lines.append('  %s = %d;' % (vn, vv))
        lines.append('}')
        lines.append('')
    return '\n'.join(lines) + '\n'


def render_field(prog, f, names, file_of, fn, pkg_of, imports):
    out = []
    if f.map:
        ec = prog.codec(f.map[1]) if f.map[1] else None
        kf = next((x for x in ec.fields if x.num == 1), None) if ec else None
        vf = next((x for x in ec.fields if x.num == 2), None) if ec else None
        kt = kf.scalar if kf and kf.scalar else 'string'
        if vf is None:
            vt = 'bytes'
        else:
            vt = type_name(prog, vf, names, file_of, fn, pkg_of)
            if vf.target and not prog.codec(vf.target).wrapper_type and file_of[vf.target] != fn:
                imports.add(file_of[vf.target])
        out.append('  map<%s, %s> %s = %d;' % (kt, vt, f.name, f.num))
        return out
    t = type_name(prog, f, names, file_of, fn, pkg_of)
    if f.target and not prog.codec(f.target).wrapper_type and file_of[f.target] != fn:
        imports.add(file_of[f.target])
    mods = ''
    if f.repeated:
        pre = 'repeated '
        if f.scalar in NUMERIC and not f.packed:
            mods = ' [packed = false]'
    elif f.optional:
        pre = 'optional '
    else:
        pre = ''
    hint = enum_hint(prog, f)
    out.append('  %s%s %s = %d%s;%s' % (pre, t, f.name, f.num, mods,
                                       ('  // enum? bale.enums.%s (inferred from name)' % hint) if hint else ''))
    return out


# ----------------------------------------------------------------------------
# Light .proto syntax checker (used for the self-check; protoc is not required)
# ----------------------------------------------------------------------------

def check_proto_syntax(text, known_types_ok=True):
    errs = []
    depth = 0
    nums = None
    in_enum = False
    names = set()
    for ln, line in enumerate(text.split('\n'), 1):
        s = line.strip()
        if not s or s.startswith('//'):
            continue
        if s.startswith('message ') and s.endswith('{'):
            depth += 1
            nums = set()
            n = s.split()[1]
            if n in names:
                errs.append('%d: duplicate message %s' % (ln, n))
            names.add(n)
        elif s.startswith('service ') and s.endswith('{'):
            depth += 1
        elif s.startswith('enum ') and s.endswith('{'):
            depth += 1
            in_enum = True
            nums = None
        elif in_enum and s != '}':
            if not re.fullmatch(r'\w+ = -?\d+;', s):
                errs.append('%d: bad enum value %r' % (ln, s))
        elif s == '}':
            depth -= 1
            nums = None
            in_enum = False
        elif nums is not None:
            m = re.fullmatch(r'(?:repeated |optional )?(?:map<[\w.]+, ?[.\w]+>|[.\w]+) (\w+) = (\d+)(?: \[packed = false\])?;(?:  // .*)?', s)
            if not m:
                errs.append('%d: bad field %r' % (ln, s))
            else:
                n = int(m.group(2))
                if n in nums or n < 1 or n > 536870911:
                    errs.append('%d: bad/duplicate field number %d' % (ln, n))
                nums.add(n)
        elif depth == 1:
            if not re.fullmatch(r'rpc \w+\((?:stream )?[.\w]+\) returns \((?:stream )?[.\w]+\);', s):
                errs.append('%d: bad rpc %r' % (ln, s))
        elif not re.match(r"(syntax|package|import) ", s):
            errs.append('%d: unexpected %r' % (ln, s))
    if depth != 0:
        errs.append('unbalanced braces')
    return errs


# ----------------------------------------------------------------------------
# Reports
# ----------------------------------------------------------------------------

def field_sig(prog, f, names):
    if f.map:
        return 'map<...>'
    if f.scalar:
        t = f.scalar
    elif f.target:
        c = prog.codec(f.target)
        t = ('google.protobuf.' + c.wrapper_type) if c.wrapper_type else names[f.target]
    else:
        t = '?'
    if f.repeated:
        t = 'repeated ' + t
    elif f.optional:
        t = 'optional ' + t
    return '%s %s = %d' % (t, f.name, f.num)


def fields_cell(prog, cod, names):
    if cod is None:
        return '?'
    if not cod.fields:
        return '(empty)'
    parts = []
    for f in cod.fields:
        if f.map:
            ec = prog.codec(f.map[1]) if f.map[1] else None
            kf = next((x for x in ec.fields if x.num == 1), None) if ec else None
            vf = next((x for x in ec.fields if x.num == 2), None) if ec else None
            kt = kf.scalar if kf and kf.scalar else '?'
            vt = (vf.scalar or (names.get(vf.target, '?') if vf and vf.target else '?')) if vf else '?'
            parts.append('map<%s,%s> %s=%d' % (kt, vt, f.name, f.num))
        else:
            parts.append(field_sig(prog, f, names).replace(' = ', '=').replace(' ', ' ', 1))
    return '; '.join(parts)


def methods_markdown(prog, services, names):
    lines = ['| # | Service | Method | Request message (fields) | Response message (fields) |',
             '|---|---------|--------|--------------------------|---------------------------|']
    i = 0
    for full in sorted(services):
        for md in services[full]:
            i += 1
            rq = prog.codec(md['req_key']) if md['req_key'] else None
            rs = prog.codec(md['res_key']) if md['res_key'] else None
            def cell(c, k):
                if c is None:
                    return '?'
                return '`%s`: %s' % (names.get(k, '?'), fields_cell(prog, c, names).replace('|', '\\|'))
            lines.append('| %d | %s | %s | %s | %s |' % (
                i, full, md['method'] + (' (stream)' if md['server_stream'] or md['client_stream'] else ''),
                cell(rq, md['req_key']), cell(rs, md['res_key'])))
    return '\n'.join(lines) + '\n'


def try_protoc(outdir):
    """Compile the generated files with protoc (or grpc_tools) when one is installed."""
    import shutil
    import subprocess
    import tempfile
    files = sorted(glob.glob(os.path.join(outdir, '*.proto')))
    tmp = tempfile.mkdtemp(prefix='bale_protoc_')
    cmds = []
    if shutil.which('protoc'):
        cmds.append(['protoc', '-I', outdir, '--descriptor_set_out=' + os.path.join(tmp, 'd.bin')] + files)
    try:
        import grpc_tools  # noqa: F401
        cmds.append([sys.executable, '-m', 'grpc_tools.protoc', '-I', outdir,
                     '--descriptor_set_out=' + os.path.join(tmp, 'd.bin')] + files)
    except ImportError:
        pass
    if not cmds:
        return None, 'no protoc / grpc_tools available (own syntax check only)'
    env = dict(os.environ, PYTHONPATH=os.pathsep.join(p for p in sys.path if p))
    r = subprocess.run(cmds[0], capture_output=True, text=True, env=env)
    shutil.rmtree(tmp, ignore_errors=True)
    return r.returncode == 0, (r.stderr.strip() or 'ok')


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument('bundle', help='bundle .js file or a directory of bundles')
    ap.add_argument('outdir')
    ap.add_argument('--methods-md', help='write a Markdown table of all gRPC methods')
    ap.add_argument('--json', help='write a JSON dump of codecs/services')
    args = ap.parse_args(argv)

    paths = sorted(glob.glob(os.path.join(args.bundle, '*.js'))) if os.path.isdir(args.bundle) else [args.bundle]
    prog = Program()
    prog.load(paths)
    prog.resolve_all()
    services = prog.collect_services()
    prog.fold_shape_wrappers({m[k] for v in services.values() for m in v for k in ('req_key', 'res_key')})
    ws_roots = find_ws_roots(prog)
    names, aliases = assign_names(prog, services, ws_roots)
    out_files, file_of, pkg_of, groups = render(prog, services, names, aliases, ws_roots, args.outdir)

    if args.methods_md:
        with open(args.methods_md, 'w') as fh:
            fh.write(methods_markdown(prog, services, names))
    if args.json:
        dump = {'codecs': [{'module': c.mid, 'local': c.name, 'name': names.get(c.key),
                            'file': file_of.get(c.key),
                            'fields': [{'num': f.num, 'name': f.name, 'sig': field_sig(prog, f, names)}
                                       for f in c.fields]} for c in prog.all_codecs()],
                'services': {k: [m['method'] for m in v] for k, v in services.items()}}
        with open(args.json, 'w') as fh:
            json.dump(dump, fh, indent=1)

    # ---- self-check (reports only; nothing is re-encoded) ----
    codecs = prog.all_codecs()
    n_methods = sum(len(v) for v in services.values())
    syntax_errs = []
    for fn, txt in out_files.items():
        for e in check_proto_syntax(txt):
            syntax_errs.append('%s:%s' % (fn, e))
    problems = [(c.key, p) for c in codecs for p in c.problems]
    unnamed = [c.key for c in codecs if c.key not in names and not c.is_map_entry and not c.wrapper_type]
    n_fields = sum(len(c.fields) for c in codecs)
    print('SELF-CHECK')
    print('  modules scanned          : %d' % len(prog.modules))
    print('  message codecs found     : %d' % len(codecs))
    print('    map entries (folded)   : %d' % sum(c.is_map_entry for c in codecs))
    print('    wrappers (folded)      : %d' % sum(bool(c.wrapper_type) for c in codecs))
    print('    messages emitted       : %d' % sum(1 for c in codecs if c.key in file_of
                                                 and not c.is_map_entry and not c.wrapper_type))
    print('    fields recovered       : %d' % n_fields)
    print('  services                 : %d' % len(services))
    print('  methods                  : %d' % n_methods)
    print('  websocket roots          : %s' % (ws_roots or 'none'))
    print('  files written            : %s' % ', '.join(sorted(out_files)))
    print('  unresolved references    : %d' % len(prog.unresolved))
    for w, e in prog.unresolved[:30]:
        print('      %s -> %s' % (w, e))
    print('  codec parse problems     : %d' % len(problems))
    for k, p in problems[:30]:
        print('      %s: %s' % (k, p))
    print('  messages without name    : %d' % len(unnamed))
    print('  proto syntax errors      : %d' % len(syntax_errs))
    for e in syntax_errs[:30]:
        print('      ' + e)
    ok, msg = try_protoc(args.outdir)
    print('  protoc compile           : %s' % ({True: 'OK', False: 'FAILED', None: 'skipped'}[ok]))
    if ok is not True:
        print('      ' + msg[:600])
    return 0 if not (prog.unresolved or syntax_errs or unnamed or ok is False) else 1


if __name__ == '__main__':
    sys.exit(main())
