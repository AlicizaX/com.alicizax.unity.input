using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace AlicizaX.Input
{
    /// <summary>
    /// 按托管引用比较的字典比较器：热键注册键是 Unity 对象或 InputAction，
    /// 不能依赖类型自己可能重写的 Equals/GetHashCode。
    /// </summary>
    internal sealed class ReferenceEqualityComparer<TKey> : IEqualityComparer<TKey>
        where TKey : class
    {
        public static readonly ReferenceEqualityComparer<TKey> Instance =
            new ReferenceEqualityComparer<TKey>();

        private ReferenceEqualityComparer()
        {
        }

        public bool Equals(TKey x, TKey y) => ReferenceEquals(x, y);

        public int GetHashCode(TKey obj) => RuntimeHelpers.GetHashCode(obj);
    }
}
