using DataLinq.Interfaces;
using System;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Metadata;

namespace DataLinq.Metadata
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
        public int? CsSize { get; set; }
        public string CsTypeName { get; set; }
        public ModelMetadata Model { get; set; }
        public PropertyInfo PropertyInfo { get; set; }
        public PropertyType Type { get; set; }
        public RelationPart RelationPart { get; set; }


        private Func<object, object> getAccessor = null;
        public object GetValue(object m)
        {
            if (getAccessor == null)
                getAccessor = BuildGetAccessor();

            return getAccessor(m);
        }

        private Action<object, object> setAccessor = null;
        public void SetValue(object m, object value)
        {
            if (setAccessor == null)
                setAccessor = BuildSetAccessor();

            setAccessor(m, Convert.ChangeType(value, CsType));
        }

        //http://geekswithblogs.net/Madman/archive/2008/06/27/faster-reflection-using-expression-trees.aspx
        private Func<object, object> BuildGetAccessor()
        {
            var instance = Expression.Parameter(typeof(object), "instance");

            UnaryExpression instanceCast = (!this.PropertyInfo.DeclaringType.IsValueType)
                ? Expression.TypeAs(instance, this.PropertyInfo.DeclaringType)
                : Expression.Convert(instance, this.PropertyInfo.DeclaringType);

            return Expression.Lambda<Func<object, object>>(
                Expression.TypeAs(Expression.Call(
                    instanceCast,
                    this.PropertyInfo.GetGetMethod()), typeof(object)), instance).Compile();
        }

        private Action<object, object> BuildSetAccessor()
        {
            ParameterExpression instance = Expression.Parameter(typeof(object), "instance");
            ParameterExpression value = Expression.Parameter(typeof(object), "value");

            // value as T is slightly faster than (T)value, so if it's not a value type, use that
            UnaryExpression instanceCast = (!this.PropertyInfo.DeclaringType.IsValueType)
                ? Expression.TypeAs(instance, this.PropertyInfo.DeclaringType)
                : Expression.Convert(instance, this.PropertyInfo.DeclaringType);

            UnaryExpression valueCast = (!this.PropertyInfo.PropertyType.IsValueType)
                ? Expression.TypeAs(value, this.PropertyInfo.PropertyType)
                : Expression.Convert(value, this.PropertyInfo.PropertyType);

            return Expression.Lambda<Action<object, object>>(
                Expression.Call(
                    instanceCast,
                    this.PropertyInfo.GetSetMethod(),
                    valueCast), new ParameterExpression[] { instance, value }).Compile();
        }

        public override string ToString()
        {
            return $"Property: {CsTypeName} {CsName}";
        }
    }
}