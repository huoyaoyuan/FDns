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

                // Arabic (Egyptian)
                yield return new object[] { "\u0644\u064A\u0647\u0645\u0627\u0628\u062A\u0643\u0644\u0645\u0648\u0634\u0639\u0631\u0628\u064A\u061F", "egbpdaj6bu4bxfgehfvwxn" };

                // Chinese (simplified)
                yield return new object[] { "\u4ED6\u4EEC\u4E3A\u4EC0\u4E48\u4E0D\u8BF4\u4E2D\u6587", "ihqwcrb4cv8a8dqg056pqjye" };
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
