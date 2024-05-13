using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace HomeBudget.Accounting.Domain.Enumerations
{
    public abstract class BaseEnumeration(int id, string name) : IComparable
    {
        public string Name { get; } = name;

        public int Id { get; } = id;

        public override string ToString() => Name;

        public static IEnumerable<T> GetAll<T>()
            where T : BaseEnumeration
        {
            return typeof(T).GetFields(BindingFlags.Public |
                                           BindingFlags.Static |
                                           BindingFlags.DeclaredOnly)
                        .Select(f => f.GetValue(null))
                        .Cast<T>();
        }

        public static T FromValue<T>(int id)
            where T : BaseEnumeration
        {
            return GetAll<T>().Single(r => r.Id == id);
        }

        public override bool Equals(object obj)
        {
            if (obj is not BaseEnumeration otherValue)
            {
                return false;
            }

            var typeMatches = GetType() == obj.GetType();
            var valueMatches = Id.Equals(otherValue.Id);

            return typeMatches && valueMatches;
        }

        public override int GetHashCode()
        {
            return Id.GetHashCode();
        }

        public int CompareTo(object obj) => Id.CompareTo(((BaseEnumeration)obj)!.Id);

        public static bool operator ==(BaseEnumeration left, BaseEnumeration right)
        {
            return left?.Equals(right) ?? ReferenceEquals(right, null);
        }

        public static bool operator !=(BaseEnumeration left, BaseEnumeration right)
        {
            return !(left == right);
        }

        public static bool operator <(BaseEnumeration left, BaseEnumeration right)
        {
            return ReferenceEquals(left, null) ? !ReferenceEquals(right, null) : left.CompareTo(right) < 0;
        }

        public static bool operator <=(BaseEnumeration left, BaseEnumeration right)
        {
            return ReferenceEquals(left, null) || left.CompareTo(right) <= 0;
        }

        public static bool operator >(BaseEnumeration left, BaseEnumeration right)
        {
            return !ReferenceEquals(left, null) && left.CompareTo(right) > 0;
        }

        public static bool operator >=(BaseEnumeration left, BaseEnumeration right)
        {
            return ReferenceEquals(left, null) ? ReferenceEquals(right, null) : left.CompareTo(right) >= 0;
        }
    }
}
