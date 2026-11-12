using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;

namespace Nerv.Core.Tests
{
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class TestAttribute : Attribute { }

    public sealed class AssertionException : Exception
    {
        public AssertionException(string message) : base(message) { }
    }

    public static class Assert
    {
        public static void True(bool condition, string message = "ожидалось true")
        {
            if (!condition) throw new AssertionException(message);
        }

        public static void False(bool condition, string message = "ожидалось false") => True(!condition, message);

        public static void Equal<T>(T expected, T actual, string what = null)
        {
            if (!Equals(expected, actual))
                throw new AssertionException((what != null ? what + ": " : "") + "ожидалось " + expected + ", получено " + actual);
        }

        public static void Near(double expected, double actual, double tolerance, string what = null)
        {
            if (double.IsNaN(actual) || Math.Abs(expected - actual) > tolerance)
                throw new AssertionException((what != null ? what + ": " : "") +
                                             "ожидалось " + expected + " ± " + tolerance + ", получено " + actual);
        }

        public static void Throws<TEx>(Action action) where TEx : Exception
        {
            try { action(); }
            catch (TEx) { return; }
            catch (Exception e) { throw new AssertionException("ожидалось " + typeof(TEx).Name + ", брошено " + e.GetType().Name); }
            throw new AssertionException("ожидалось исключение " + typeof(TEx).Name);
        }
    }

    public static class TestRunner
    {
        public static int Main(string[] args)
        {
            string filter = args.Length > 0 ? args[0] : null;
            var tests = typeof(TestRunner).Assembly.GetTypes()
                .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static))
                .Where(m => m.GetCustomAttribute<TestAttribute>() != null)
                .Where(m => filter == null || (m.DeclaringType.Name + "." + m.Name).Contains(filter))
                .OrderBy(m => m.DeclaringType.Name).ThenBy(m => m.Name)
                .ToList();

            int failed = 0;
            var total = Stopwatch.StartNew();
            foreach (MethodInfo m in tests)
            {
                string name = m.DeclaringType.Name + "." + m.Name;
                var sw = Stopwatch.StartNew();
                try
                {
                    m.Invoke(null, null);
                    Console.WriteLine("  ✓ " + name + "  (" + sw.ElapsedMilliseconds + " мс)");
                }
                catch (TargetInvocationException tie)
                {
                    failed++;
                    Exception e = tie.InnerException ?? tie;
                    Console.WriteLine("  ✗ " + name + "\n      " + e.GetType().Name + ": " + e.Message);
                    if (!(e is AssertionException)) Console.WriteLine(e.StackTrace);
                }
            }

            Console.WriteLine();
            Console.WriteLine((tests.Count - failed) + "/" + tests.Count + " тестов прошло за " + total.ElapsedMilliseconds + " мс");
            return failed == 0 ? 0 : 1;
        }
    }
}
