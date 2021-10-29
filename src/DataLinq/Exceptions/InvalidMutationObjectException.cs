using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace DataLinq.Exceptions
{
    public class InvalidMutationObjectException : System.Exception
    {
        private readonly string message;

        public InvalidMutationObjectException(string message)
        {
            this.message = message + " ";
        }

        public InvalidMutationObjectException() : base()
        {
        }

        public InvalidMutationObjectException(string message, Exception innerException) : base(message, innerException)
        {
        }

        public override string Message
        {
            get
            {
                return "The client query is invalid: " + message;
            }
        }
    }
}
