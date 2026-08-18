using System;

namespace AlicizaX.UI.UXNavigation
{
    public static class UXFocusChange
    {
        public enum Cause : byte
        {
            User = 0,
            Programmatic = 1,
        }

        private static int _programmaticCount;

        public static Cause Current => _programmaticCount > 0 ? Cause.Programmatic : Cause.User;

        public static void Begin(Cause cause)
        {
            if (cause == Cause.Programmatic)
            {
                _programmaticCount++;
            }
        }

        public static void End(Cause cause)
        {
            if (cause == Cause.Programmatic && _programmaticCount > 0)
            {
                _programmaticCount--;
            }
        }

        public readonly struct Scope : IDisposable
        {
            private readonly Cause _cause;

            public Scope(Cause cause)
            {
                _cause = cause;
                Begin(cause);
            }

            public void Dispose()
            {
                End(_cause);
            }
        }
    }
}
