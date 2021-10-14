using System.Collections.Generic;
using Xunit;

namespace Meowtrix.FDns.UnitTests
{
    public class PunyCodeTests
    {
        public static IEnumerable<object[]> TestData
        {
            get
            {
                yield return new object[] { "", "" };
                yield return new object[] { "abc", "abc" };
                yield return new object[] { "0123456789", "0123456789" };
            }
        }

        [Theory]
        [MemberData(nameof(TestData))]
        public void TestEncoding(string raw, string expected)
        {
            Assert.Equal(expected, PunyCode.EncodeToString(raw));
        }
    }
}
