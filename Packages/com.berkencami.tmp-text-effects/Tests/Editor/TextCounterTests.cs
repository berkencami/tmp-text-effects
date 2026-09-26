using System.Reflection;
using NUnit.Framework;

namespace TMPTextEffects.Tests
{
    public class TextCounterTests : TextTestFixture
    {
        private TextCounter CreateCounter(string format, string prefix = "", string suffix = "")
        {
            var tmp = CreateText("");
            var counter = tmp.gameObject.AddComponent<TextCounter>();
            Set(counter, "_Format", format);
            Set(counter, "_Prefix", prefix);
            Set(counter, "_Suffix", suffix);
            return counter;
        }

        private static void Set(object target, string field, object value) =>
            target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(target, value);

        private static string Shown(TextCounter counter) => counter.GetComponent<TMPro.TMP_Text>().text;

        [TestCase("N0", 1234567, "1,234,567")]
        [TestCase("N0", -1234.4, "-1,234")]
        [TestCase("N0", 0, "0")]
        [TestCase("N0", 999.5, "1,000")]
        [TestCase("F0", 1234567, "1234567")]
        [TestCase("N2", 1234.5, "1,234.50")]
        public void Formats(string format, double value, string expected)
        {
            var counter = CreateCounter(format);
            counter.Value = value;
            Assert.That(Shown(counter), Is.EqualTo(expected));
        }

        [Test]
        public void PrefixAndSuffix_WrapTheNumber()
        {
            var counter = CreateCounter("N0", "$", " coins");
            counter.Value = 2500;
            Assert.That(Shown(counter), Is.EqualTo("$2,500 coins"));
        }
    }
}
