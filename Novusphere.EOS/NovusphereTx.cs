using System;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json;
using NBitcoin;
using NBitcoin.Crypto;
using NBitcoin.DataEncoders;

namespace Novusphere.EOS
{
    public class NovusphereTx
    {
        public const int NETWORK_ATMOS = 1;

        [JsonProperty("net")]
        public int Network { get; set; }
        [JsonProperty("key")]
        public string PublicKey { get; set; }
        [JsonProperty("sig")]
        public string Signature { get; set; }
        [JsonProperty("op")]
        public string Operation { get; set; }
        [JsonProperty("data")]
        public string Data { get; set; }
        [JsonProperty("nonce")]
        public int Nonce { get; set; }

        private uint256 GetHash256()
        {
            byte[] opdata = Encoding.UTF8.GetBytes(Operation + Data);
            byte[] data = new byte[8 + opdata.Length];
            Array.Copy(BitConverter.GetBytes(Network), 0, data, 0, 4);
            Array.Copy(BitConverter.GetBytes(Nonce), 0, data, 4, 4);
            Array.Copy(opdata, 0, data, 8, opdata.Length);

            var hash = new uint256(Hashes.SHA256(data));
            return hash;
        }

        public void Sign(Key key)
        {
            PublicKey = key.PubKey.ToHex();
            Signature = Encoders.Hex.EncodeData(key.Sign(GetHash256()).ToDER());
        }

        public bool ValidateSignature()
        {
            var pubKey = new PubKey(PublicKey);
            var derSig = Encoders.Hex.DecodeData(Signature);
            var hash = GetHash256();
            var check = pubKey.Verify(hash, derSig);
            return check;
        }

        public string ToJson()
        {
            return JsonConvert.SerializeObject(this);
        }
    }
}
