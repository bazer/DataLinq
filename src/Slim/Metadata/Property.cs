using Slim.Interfaces;
using System;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Metadata;

namespace Slim.Metadata
{
    public enum PropertyType
    {
        Value,
        Relation
    }

    public class Property
    {
        public object[] Attributes { get; set; }
        public Column Column { get; set; }
        public string CsName { get; set; }
        public bool CsNullable { get; set; }
        public Type CsType { get; set; }
        public string CsTypeName { get; set; }
        public Model Model { get; set; }
        public PropertyInfo PropertyInfo { get; set; }
        public PropertyType Type { get; set; }
        public RelationPart RelationPart { get; set; }

        private Func<object, object> accessor = null;
        public object GetValue(object m)
        {
            if (accessor == null)
                accessor = BuildAccessor(PropertyInfo.GetGetMethod(true));

            return accessor(m);
        }

        private static Func<object, object> BuildAccessor(MethodInfo method)
        {
            ParameterExpression obj = Expression.Parameter(typeof(object), "obj");

            Expression<Func<object, object>> expr =
                Expression.Lambda<Func<object, object>>(
                    Expression.Convert(
                        Expression.Call(
                            Expression.Convert(obj, method.DeclaringType),
                            method),
                        typeof(object)),
                    obj);

            return expr.Compile();
        }
    }
}