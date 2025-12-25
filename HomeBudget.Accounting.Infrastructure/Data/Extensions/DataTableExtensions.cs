using System;
using System.Collections.Generic;
using System.Data;
using System.Reflection;

namespace HomeBudget.Accounting.Infrastructure.Data.Extensions
{
    public static class DataTableExtensions
    {
        public static DataTable ToDataTable<T>(this T origin)
            where T : IDbEntity
        {
            return new[] { origin }.ToDataTable<T>();
        }

        public static DataTable ToDataTable<T>(this IEnumerable<T> collection)
            where T : IDbEntity
        {
            var dataTable = new DataTable(typeof(T).Name);

            // Get properties of the type
            var properties = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance);

            // Define columns
            foreach (var property in properties)
            {
                dataTable.Columns.Add(property.Name, Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType);
            }

            // Add rows
            foreach (var item in collection)
            {
                var values = new object[properties.Length];

                for (var i = 0; i < properties.Length; i++)
                {
                    values[i] = properties[i].GetValue(item);
                }

                dataTable.Rows.Add(values);
            }

            return dataTable;
        }
    }
}
