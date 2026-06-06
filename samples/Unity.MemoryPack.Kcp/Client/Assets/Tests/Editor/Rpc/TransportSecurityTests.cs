using System;
using System.Linq;
using Lakona.Rpc.Core;
using NUnit.Framework;

namespace Tests.Editor.Rpc
{
    public class TransportSecurityTests
    {
        [Test]
        public void CompressionOnly_Roundtrip()
        {
            var cfg = new TransportSecurityConfig
            {
                EnableCompression = true,
                CompressionThresholdBytes = 0
            };

            var codec = new TransportFrameCodec(cfg);
            var input = Enumerable.Repeat((byte)0x5A, 4096).ToArray();

            var encoded = codec.Encode(input);
            var decoded = codec.Decode(encoded);

            Assert.AreEqual(input, decoded.ToArray());
        }

        [Test]
        public void EncryptionOnly_Roundtrip()
        {
            var cfg = new TransportSecurityConfig
            {
                EnableEncryption = true,
                EncryptionKey = BuildKey()
            };

            var codec = new TransportFrameCodec(cfg);
            var input = BuildPayload();

            var encoded = codec.Encode(input);
            var decoded = codec.Decode(encoded);

            Assert.AreEqual(input, decoded.ToArray());
        }

        [Test]
        public void CompressionAndEncryption_Roundtrip()
        {
            var cfg = new TransportSecurityConfig
            {
                EnableCompression = true,
                CompressionThresholdBytes = 0,
                EnableEncryption = true,
                EncryptionKey = BuildKey()
            };

            var codec = new TransportFrameCodec(cfg);
            var input = Enumerable.Repeat((byte)0xAB, 8192).ToArray();

            var encoded = codec.Encode(input);
            var decoded = codec.Decode(encoded);

            Assert.AreEqual(input, decoded.ToArray());
        }

        private static byte[] BuildKey()
        {
            return Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();
        }

        private static byte[] BuildPayload()
        {
            var text = "hello-secure-transport";
            return System.Text.Encoding.UTF8.GetBytes(text);
        }
    }
}
