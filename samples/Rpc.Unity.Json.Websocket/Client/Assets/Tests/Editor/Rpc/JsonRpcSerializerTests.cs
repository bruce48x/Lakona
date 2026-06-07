using System;
using Game.Rpc.Contracts;
using Lakona.Rpc.Core;
using Lakona.Rpc.Serializer.Json;
using NUnit.Framework;
using NUnitAssert = NUnit.Framework.Assert;

namespace Tests.Editor.Rpc
{
    public class JsonRpcSerializerTests
    {
        [Test]
        public void JsonSerializer_RoundTrip_LoginRequest()
        {
            var serializer = new JsonRpcSerializer();
            var input = new LoginRequest
            {
                Account = "demo",
                Password = "secret"
            };

            using var bytes = serializer.SerializeFrame(input);
            var output = serializer.Deserialize<LoginRequest>(bytes.Memory);

            NUnitAssert.AreEqual(input.Account, output.Account);
            NUnitAssert.AreEqual(input.Password, output.Password);
        }

        [Test]
        public void JsonSerializer_RoundTrip_LoginReply()
        {
            var serializer = new JsonRpcSerializer();
            var input = new LoginReply
            {
                Code = 200,
                Token = "abc"
            };

            using var bytes = serializer.SerializeFrame(input);
            var output = serializer.Deserialize<LoginReply>(bytes.Memory);

            NUnitAssert.AreEqual(input.Code, output.Code);
            NUnitAssert.AreEqual(input.Token, output.Token);
        }
    }
}
