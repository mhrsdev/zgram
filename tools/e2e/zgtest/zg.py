"""Raw Zerogram zg.* requests for Telethon (the stock schema does not know them)."""
import struct
from telethon.tl.tlobject import TLRequest, TLObject
from telethon.extensions import BinaryReader


def _tlstr(s):
    return TLObject.serialize_bytes(s)


class _Req(TLRequest):
    CONSTRUCTOR_ID = 0
    SUBCLASS_OF_ID = 0
    async def resolve(self, client, utils):
        pass


class Registration:
    def __init__(self, issuer, username, display_name, otpauth_uri, manual_key, period, digits):
        self.issuer, self.username, self.display_name = issuer, username, display_name
        self.otpauth_uri, self.manual_key, self.period, self.digits = otpauth_uri, manual_key, period, digits

    @classmethod
    def from_reader(cls, r):
        return cls(r.tgread_string(), r.tgread_string(), r.tgread_string(), r.tgread_string(),
                   r.tgread_string(), r.read_int(), r.read_int())


class Activated:
    def __init__(self, recovery_codes, authorization):
        self.recovery_codes, self.authorization = recovery_codes, authorization

    @classmethod
    def from_reader(cls, r):
        assert r.read_int(signed=False) == 0x1cb5c415
        codes = [r.tgread_string() for _ in range(r.read_int())]
        return cls(codes, r.tgread_object())


class Policy:
    def __init__(self, group_member_limit, queue_ttl_days, single_device):
        self.group_member_limit, self.queue_ttl_days, self.single_device = group_member_limit, queue_ttl_days, single_device

    @classmethod
    def from_reader(cls, r):
        return cls(r.read_int(), r.read_int(), r.tgread_bool())


class AckResult:
    def __init__(self, acked):
        self.acked = acked

    @classmethod
    def from_reader(cls, r):
        return cls(r.read_int())


def _strs(*values):
    return b"".join(_tlstr(v) for v in values)


class Register(_Req):
    CONSTRUCTOR_ID = 0xbab09633
    def __init__(self, username, display_name, email):
        self.username, self.display_name, self.email = username, display_name, email
    def _bytes(self):
        return struct.pack('<I', self.CONSTRUCTOR_ID) + _strs(self.username, self.display_name, self.email)
    @staticmethod
    def read_result(reader):
        assert reader.read_int(signed=False) == 0xb0e28bc4
        return Registration.from_reader(reader)


class Activate(_Req):
    CONSTRUCTOR_ID = 0x1c5b95c7
    def __init__(self, username, code):
        self.username, self.code = username, code
    def _bytes(self):
        return struct.pack('<I', self.CONSTRUCTOR_ID) + _strs(self.username, self.code)
    @staticmethod
    def read_result(reader):
        assert reader.read_int(signed=False) == 0x9fec1e3b
        return Activated.from_reader(reader)


class Login(_Req):
    CONSTRUCTOR_ID = 0x95ceac5e
    def __init__(self, username, code):
        self.username, self.code = username, code
    def _bytes(self):
        return struct.pack('<I', self.CONSTRUCTOR_ID) + _strs(self.username, self.code)
    @staticmethod
    def read_result(reader):
        return reader.tgread_object()


class Recover(_Req):
    CONSTRUCTOR_ID = 0x6093a2fc
    def __init__(self, username, recovery_code):
        self.username, self.recovery_code = username, recovery_code
    def _bytes(self):
        return struct.pack('<I', self.CONSTRUCTOR_ID) + _strs(self.username, self.recovery_code)
    @staticmethod
    def read_result(reader):
        assert reader.read_int(signed=False) == 0xb0e28bc4
        return Registration.from_reader(reader)


class GetPolicy(_Req):
    CONSTRUCTOR_ID = 0x36d005e7
    def _bytes(self):
        return struct.pack('<I', self.CONSTRUCTOR_ID)
    @staticmethod
    def read_result(reader):
        assert reader.read_int(signed=False) == 0xc7220c7c
        return Policy.from_reader(reader)


class AckMessages(_Req):
    CONSTRUCTOR_ID = 0x84ff4d33
    def __init__(self, ids):
        self.ids = list(ids)
    def _bytes(self):
        return (struct.pack('<I', self.CONSTRUCTOR_ID) + struct.pack('<Ii', 0x1cb5c415, len(self.ids))
                + b"".join(struct.pack('<i', i) for i in self.ids))
    @staticmethod
    def read_result(reader):
        assert reader.read_int(signed=False) == 0xfdadd943
        return AckResult.from_reader(reader)


class DeliveryState:
    def __init__(self, marks): self.marks = marks
    def __repr__(self): return f"DeliveryState({self.marks})"
    @classmethod
    def from_reader(cls, r):
        assert r.read_int(signed=False) == 0x1cb5c415
        marks = []
        for _ in range(r.read_int()):
            assert r.read_int(signed=False) == 0x097979b8
            peer = r.tgread_object(); marks.append((peer, r.read_int()))
        return cls(marks)


class GetDeliveryState(_Req):
    CONSTRUCTOR_ID = 0xa65929cc
    def _bytes(self): return struct.pack('<I', self.CONSTRUCTOR_ID)
    @staticmethod
    def read_result(reader):
        assert reader.read_int(signed=False) == 0xe3e0513e
        return DeliveryState.from_reader(reader)
