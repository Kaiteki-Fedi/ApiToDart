using System;

namespace ApiToDart.Dart
{
    [Flags]
    public enum ElementFlags
    {
        None = 0,
        Const,
        Final,
        Late,
    }
}
