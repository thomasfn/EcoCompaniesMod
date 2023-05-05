using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Eco.Mods.Companies
{
    public static class EnumerableExt
    {
        public static bool SetEquals<T>(this IEnumerable<T> a, IEnumerable<T> b)
        {
            a ??= Enumerable.Empty<T>();
            b ??= Enumerable.Empty<T>();
            return a.All(x => b.Contains(x)) && b.All(x => a.Contains(x));
        }
    }
}
