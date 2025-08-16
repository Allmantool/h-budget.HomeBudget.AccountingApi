using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;

namespace HomeBudget.Accounting.Domain.Enumerations
{
    public abstract class BaseEnumeration<TEnum, TValue> : IComparable<TEnum>
        where TEnum : BaseEnumeration<TEnum, TValue>
        where TValue : notnull, IComparable
    {
        private static readonly ConcurrentDictionary<TValue, TEnum> _instances = new();

        public string Name { get; }
        public TValue Key { get; }

        protected BaseEnumeration(TValue key, string name)
        {
            Key = key;
            Name = name;

            _instances.TryAdd(key, (TEnum)this);
        }

        static BaseEnumeration()
        {
            foreach (var field in typeof(TEnum).GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                _ = field.GetValue(null);
            }
        }

        public static IEnumerable<TEnum> GetAll() => _instances.Values;

        public static TEnum FromValue(TValue value)
        {
            if (_instances.TryGetValue(value, out var result))
            {
                return result;
            }

            throw new ArgumentException($"No {typeof(TEnum).Name} with value '{value}' found.");
        }

        public static bool TryFromValue(TValue value, out TEnum result) => _instances.TryGetValue(value, out result);

        public override string ToString() => Name;

        public override bool Equals(object obj)
        {
            return obj is BaseEnumeration<TEnum, TValue> other &&
                   GetType() == obj.GetType() &&
                   EqualityComparer<TValue>.Default.Equals(Key, other.Key);
        }

        public override int GetHashCode() => HashCode.Combine(Key);

        public int CompareTo(TEnum other) => Key.CompareTo(other.Key);

        public static bool operator ==(BaseEnumeration<TEnum, TValue> left, BaseEnumeration<TEnum, TValue> right)
            => Equals(left, right);

        public static bool operator !=(BaseEnumeration<TEnum, TValue> left, BaseEnumeration<TEnum, TValue> right)
            => !Equals(left, right);

        public static bool operator <(BaseEnumeration<TEnum, TValue> left, BaseEnumeration<TEnum, TValue> right)
            => left is null ? right is not null : left.CompareTo((TEnum)right) < 0;

        public static bool operator <=(BaseEnumeration<TEnum, TValue> left, BaseEnumeration<TEnum, TValue> right)
            => left is null || left.CompareTo((TEnum)right) <= 0;

        public static bool operator >(BaseEnumeration<TEnum, TValue> left, BaseEnumeration<TEnum, TValue> right)
            => left is not null && left.CompareTo((TEnum)right) > 0;

        public static bool operator >=(BaseEnumeration<TEnum, TValue> left, BaseEnumeration<TEnum, TValue> right)
            => left is null ? right is null : left.CompareTo((TEnum)right) >= 0;
    }
}
