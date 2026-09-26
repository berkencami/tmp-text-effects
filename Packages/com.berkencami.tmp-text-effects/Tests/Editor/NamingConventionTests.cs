using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Serialization;

namespace TMPTextEffects.Tests
{
    /// <summary>
    /// Guards the package's field naming: public fields PascalCase, private fields _camelCase, serialized private
    /// fields _PascalCase, and no [FormerlySerializedAs] (renames migrate the data instead).
    /// </summary>
    public class NamingConventionTests
    {
        private static readonly Type[] _types = typeof(LayeredText).Assembly.GetTypes()
            .Where(t => t.Namespace == "TMPTextEffects" && !t.IsDefined(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute)))
            .ToArray();

        private static FieldInfo[] DeclaredFields(Type t) =>
            t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Where(f => !f.IsDefined(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute)) && !f.IsLiteral)
                .Where(f => t.GetEvent(f.Name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static) == null) // event backing fields
                .ToArray();

        [Test]
        public void NoFormerlySerializedAs()
        {
            var offenders = _types.SelectMany(DeclaredFields)
                .Where(f => f.IsDefined(typeof(FormerlySerializedAsAttribute)))
                .Select(f => $"{f.DeclaringType.Name}.{f.Name}");
            Assert.That(offenders, Is.Empty);
        }

        [Test]
        public void FieldNamesFollowTheConvention()
        {
            var offenders = _types.SelectMany(DeclaredFields).Where(f => !Follows(f))
                .Select(f => $"{f.DeclaringType.Name}.{f.Name}");
            Assert.That(offenders, Is.Empty);
        }

        private static bool Follows(FieldInfo f)
        {
            if (f.DeclaringType.IsEnum) return true;
            string n = f.Name;
            if (f.IsPublic) return char.IsUpper(n[0]);

            bool serialized = f.IsDefined(typeof(SerializeField)) || f.IsDefined(typeof(SerializeReference));
            return n.Length > 1 && n[0] == '_' && (serialized ? char.IsUpper(n[1]) : char.IsLower(n[1]));
        }
    }
}
