using System;
using System.Diagnostics.CodeAnalysis;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace NullGuard.Tests {

    public interface INoAttrInterface { }

    public class NotInterface { }

    [NullSafe]
    public interface IDuplicitAttrInterface {

        [CanBeNull, NeverNull]
        string Name { get; }

    }

    [NullSafe]
    public interface ICorrectInterface {

        [NeverNull]
        string Name { get; }

        string Description {
            [CanBeNull] get;
            [NeverNull] set;
        }

        [CanBeNull]
        string IconUrl { get; }

        [NeverNull]
        string ToXml(string xmlVersion, [NeverNull] out string[] errors);

    }

    public class CorrectImplementation : ICorrectInterface {

        public string Name => "CorrectImplementation";

        public string Description { get; set; } = null;

        public string IconUrl => null;

        public string ToXml(string xmlVersion, out string[] errors) {
            errors = new string[0];
            return $"<some-xml version={xmlVersion}/>";
        }

    }

    public class IncorrectImplementation : ICorrectInterface {

        public string Name => null;

        public string Description { get; set; } = null;

        public string IconUrl => null;

        public string ToXml(string xmlVersion, out string[] errors) {
            errors = null;
            return "";
        }

    }

    [NullSafe]
    public interface INullablesInterface {

        [NeverNull]
        char? Character { get; }

        int? Integer {
            [NeverNull] get;
            [NeverNull] set;
        }

        [NeverNull]
        bool? And([CanBeNull] bool? a, [CanBeNull] bool? b);

        [CanBeNull]
        bool? Or([NeverNull] bool? a, [NeverNull] bool? b);

    }

    [SuppressMessage("ReSharper", "PossibleInvalidOperationException")]
    internal class CorrectNullablesImplementation : INullablesInterface {

        public char? Character => 'a';

        private int integer;

        public int? Integer {
            get => integer;
            set => integer = value.Value;
        }

        public bool? And(bool? a, bool? b) => a == true && b == true;

        public bool? Or(bool? a, bool? b) => a.Value || b.Value;

    }

    internal class IncorrectNullablesImplementation : INullablesInterface {

        public char? Character => null;

        public int? Integer {
            get => null;
            set { }
        }

        public bool? And(bool? a, bool? b) => null;

        public bool? Or(bool? a, bool? b) => null;

    }

    [TestClass]
    public class NullSafety_Tests {

        [TestMethod, ExpectedException(typeof(InvalidOperationException))]
        public void Prepare_NoAttributeTest() {
            NullSafety<INoAttrInterface>.Prepare();
        }

        [TestMethod, ExpectedException(typeof(InvalidOperationException))]
        public void Prepare_NotInterfaceTest() {
            NullSafety<NotInterface>.Prepare();
        }

        [TestMethod, ExpectedException(typeof(InvalidOperationException))]
        public void Prepare_DuplicitAttrTest() {
            NullSafety<IDuplicitAttrInterface>.Prepare();
        }

        [TestMethod]
        public void Prepare_OkTest() {
            NullSafety<ICorrectInterface>.Prepare();
        }

        [TestMethod]
        [SuppressMessage("ReSharper", "NotAccessedVariable")]
        public void Guard_OkTest() {
            var instance = new CorrectImplementation();
            var guardedInstance = NullSafety<ICorrectInterface>.Guard(instance);
            Console.WriteLine(guardedInstance.Description ?? "null");
            guardedInstance.Description = "";
            Console.WriteLine(guardedInstance.Description ?? "null");
            Console.WriteLine(guardedInstance.Name);
            Console.WriteLine(guardedInstance.IconUrl ?? "null");
            guardedInstance.ToXml("1.1", out _);
        }

        [TestMethod]
        public void Guard_BadUsageTest() {
            var instance = new CorrectImplementation();
            var guardedInstance = NullSafety<ICorrectInterface>.Guard(instance);
            Assert.ThrowsException<NullSafetyViolationException>(() => { guardedInstance.Description = null; });
            Assert.ThrowsException<NullSafetyViolationException>(() => { guardedInstance.ToXml(null, out _); });
        }

        [TestMethod]
        public void Guard_BadImplementationTest() {
            var instance = new IncorrectImplementation();
            var guardedInstance = NullSafety<ICorrectInterface>.Guard(instance);
            Assert.ThrowsException<NullSafetyViolationException>(() => { Console.WriteLine(guardedInstance.Name); });
            Assert.ThrowsException<NullSafetyViolationException>(() => { guardedInstance.ToXml("1.1", out _); });
        }

        [TestMethod]
        [SuppressMessage("ReSharper", "NotAccessedVariable")]
        public void Guard_NullablesOkTest() {
            var instance = new CorrectNullablesImplementation();
            var guardedInstance = NullSafety<INullablesInterface>.Guard(instance);
            Console.WriteLine(guardedInstance.Character);
            Console.WriteLine(guardedInstance.Integer);
            guardedInstance.Integer = 15;
            Console.WriteLine(guardedInstance.Integer);
            Console.WriteLine(guardedInstance.And(null, true));
            Console.WriteLine(guardedInstance.Or(false, true));

        }

        [TestMethod]
        public void Guard_NullablesBadUsageTest() {
            var instance = new CorrectNullablesImplementation();
            var guardedInstance = NullSafety<INullablesInterface>.Guard(instance);
            Assert.ThrowsException<NullSafetyViolationException>(() => { guardedInstance.Integer = null; });
            Assert.ThrowsException<NullSafetyViolationException>(() => { guardedInstance.Or(null, false); });
        }

        [TestMethod]
        public void Guard_NullablesBadImplementationTest() {
            var instance = new IncorrectNullablesImplementation();
            var guardedInstance = NullSafety<INullablesInterface>.Guard(instance);
            Assert.ThrowsException<NullSafetyViolationException>(() => { Console.WriteLine(guardedInstance.Character); });
            Assert.ThrowsException<NullSafetyViolationException>(() => { Console.WriteLine(guardedInstance.And(null, true)); });
            Assert.ThrowsException<NullSafetyViolationException>(() => { Console.WriteLine(guardedInstance.Integer); });
        }

    }

}
