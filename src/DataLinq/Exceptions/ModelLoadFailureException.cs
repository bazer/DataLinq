using System;
using DataLinq.Instances;

namespace DataLinq.Exceptions
{
    class ModelLoadFailureException : Exception
    {
        public IKey Key { get; }
        private readonly string? message;

        public ModelLoadFailureException(IKey key, string message)
        {
            Key = key;
            this.message = message;
        }

        public ModelLoadFailureException(IKey key) : base()
        {
            Key = key;
        }

        public ModelLoadFailureException(IKey key, string message, Exception innerException) : base(message, innerException)
        {
            Key = key;
        }

        public override string Message => string.IsNullOrWhiteSpace(message)
            ? $"Couldn't load model with id '{Key}'"
            : $"Couldn't load model with id '{Key}': {message}";
    }
}
