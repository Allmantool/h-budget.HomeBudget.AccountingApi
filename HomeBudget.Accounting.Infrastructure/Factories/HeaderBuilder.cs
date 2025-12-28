using System;
using System.Text;

using Confluent.Kafka;

namespace HomeBudget.Accounting.Infrastructure.Factories
{
    public class HeaderBuilder
    {
        private readonly Headers _headers = new Headers();
        private readonly Encoding _defaultEncoding = Encoding.UTF8;

        public HeaderBuilder With(string key, string value)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException("Header key cannot be null or empty", nameof(key));
            }

            if (string.IsNullOrWhiteSpace(value))
            {
                return this;
            }

            return With(key, _defaultEncoding.GetBytes(value));
        }

        public HeaderBuilder With(string key, byte[] value)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException("Header key cannot be null or empty", nameof(key));
            }

            if (value == null || value.Length == 0)
            {
                return this;
            }

            _headers.Add(key, value);
            return this;
        }

        public Headers Build() => _headers;
    }
}
