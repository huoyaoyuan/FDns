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

                // Chinese (traditional)
                yield return new object[] { "\u4ED6\u5011\u7232\u4EC0\u9EBD\u4E0D\u8AAA\u4E2D\u6587", "ihqwctvzc91f659drss3x8bo0yb" };

                // Czech
                yield return new object[] { "\u0050\u0072\u006F\u010D\u0070\u0072\u006F\u0073\u0074\u011B\u006E\u0065\u006D\u006C\u0075\u0076\u00ED\u010D\u0065\u0073\u006B\u0079", "Proprostnemluvesky-uyb24dma41a" };

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
