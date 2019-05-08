using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Slim.Exceptions
{
    public class InvalidQueryException : System.Exception
    {
        private readonly string message;

        public InvalidQueryException(string message)
        {
            this.message = message + " ";
        }

        public InvalidQueryException() : base()
        {
        }

        public InvalidQueryException(string message, Exception innerException) : base(message, innerException)
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
