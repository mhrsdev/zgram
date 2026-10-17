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


# ---------------------------------------------------------------- owner / group tokens

class OwnerChallenge:
    def __init__(self, server_nonce, salt, iterations, server_proof):
        self.server_nonce, self.salt, self.iterations, self.server_proof = server_nonce, salt, iterations, server_proof

    @classmethod
    def from_reader(cls, r):
        return cls(r.tgread_bytes(), r.tgread_bytes(), r.read_int(), r.tgread_bytes())


class GroupToken:
    def __init__(self, chat_id, title, token, members, date):
        self.chat_id, self.title, self.token, self.members, self.date = chat_id, title, token, members, date

    @classmethod
    def from_reader(cls, r):
        return cls(r.read_long(), r.tgread_string(), r.tgread_string(), r.read_int(), r.read_int())


class OwnerInfo:
    def __init__(self, server_name, connect_message, groups):
        self.server_name, self.connect_message, self.groups = server_name, connect_message, groups

    @classmethod
    def from_reader(cls, r):
        name, message = r.tgread_string(), r.tgread_string()
        assert r.read_int(signed=False) == 0x1cb5c415
        groups = []
        for _ in range(r.read_int()):
            assert r.read_int(signed=False) == 0x1fce1382
            groups.append(GroupToken.from_reader(r))
        return cls(name, message, groups)


class OwnerHello(_Req):
    CONSTRUCTOR_ID = 0x07bab1d5
    def __init__(self, client_nonce): self.client_nonce = client_nonce
    def _bytes(self): return struct.pack('<I', self.CONSTRUCTOR_ID) + _tlstr(self.client_nonce)
    @staticmethod
    def read_result(reader):
        assert reader.read_int(signed=False) == 0x487d9d47
        return OwnerChallenge.from_reader(reader)


class OwnerLogin(_Req):
    CONSTRUCTOR_ID = 0x9f36102e
    def __init__(self, client_nonce, server_nonce, client_proof):
        self.client_nonce, self.server_nonce, self.client_proof = client_nonce, server_nonce, client_proof
    def _bytes(self):
        return struct.pack('<I', self.CONSTRUCTOR_ID) + _tlstr(self.client_nonce) + _tlstr(self.server_nonce) + _tlstr(self.client_proof)
    @staticmethod
    def read_result(reader): return reader.tgread_object()


class GetOwnerInfo(_Req):
    CONSTRUCTOR_ID = 0xb2801d1e
    def _bytes(self): return struct.pack('<I', self.CONSTRUCTOR_ID)
    @staticmethod
    def read_result(reader):
        assert reader.read_int(signed=False) == 0x66b6c5a0
        return OwnerInfo.from_reader(reader)


class GetGroupToken(_Req):
    CONSTRUCTOR_ID = 0x36dbb88a
    def __init__(self, chat_id): self.chat_id = chat_id
    def _bytes(self): return struct.pack('<Iq', self.CONSTRUCTOR_ID, self.chat_id)
    @staticmethod
    def read_result(reader):
        assert reader.read_int(signed=False) == 0x1fce1382
        return GroupToken.from_reader(reader)


class RotateGroupToken(GetGroupToken):
    CONSTRUCTOR_ID = 0x478a1fe1


class JoinGroup(_Req):
    CONSTRUCTOR_ID = 0x03c02002
    def __init__(self, token): self.token = token
    def _bytes(self): return struct.pack('<I', self.CONSTRUCTOR_ID) + _tlstr(self.token)
    @staticmethod
    def read_result(reader): return reader.tgread_object()
