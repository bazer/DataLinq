using System;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Slim.Interfaces;

namespace Slim.Linq
{
    public abstract class QueryProvider : IQueryProvider
    {
        protected QueryProvider()
        {
        }

        public IQueryable<T> CreateQuery<T>(Expression expression)
        {
            return new Query<T>(this, expression);
        }

        IQueryable IQueryProvider.CreateQuery(Expression expression)
        {
            Type elementType = TypeSystem.GetElementType(expression.Type);

            try
            {
                return (IQueryable)Activator.CreateInstance(typeof(DbRead<>).MakeGenericType(elementType), new object[] { this, expression });
            }
            catch (TargetInvocationException tie)
            {
                throw tie.InnerException;
            }
        }

        S IQueryProvider.Execute<S>(Expression expression)
        {
            return (S)this.Execute(expression);
        }

        object IQueryProvider.Execute(Expression expression)
        {
            return this.Execute(expression);
        }

        public abstract object Execute(Expression expression);

        public abstract string GetQueryText(Expression expression);
        
    }
}