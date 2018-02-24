using Castle.DynamicProxy;
using Slim.Interfaces;
using Slim.Metadata;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Slim.Instances
{
    public static class InstanceFactory
    {
        private static ProxyGenerator generator = new ProxyGenerator();

        public static T NewImmutableRow<T>(RowData instanceData) // where T : class, IModel
        {
            return (T)generator.CreateInterfaceProxyWithoutTarget(typeof(T), new ImmutableRowInterceptor(instanceData));
            //return generator.CreateInterfaceProxyWithoutTarget<T>(new ImmutableRowInterceptor(instanceData));
        }

        public static T NewDatabase<T>(DatabaseProvider databaseProvider) where T : class, IDatabaseModel
        {
            return generator.CreateInterfaceProxyWithoutTarget<T>(new DatabaseInterceptor(databaseProvider));
        }
    }

    //internal static class MutableInstanceCreator<M> where M : class, IModl
    //{
    //    private static ProxyGenerator generator = new ProxyGenerator();

    //    internal static M NewInstance(M immutableInstance)// where T : class
    //    {
    //        if (!(immutableInstance is IMutable))
    //            throw new Exception("Object must inherit from IMutable");

    //        //var generator = new ProxyGenerator();
    //        var proxy = generator.CreateInterfaceProxyWithoutTarget(typeof(M), new MutableInterceptor((IMutable)immutableInstance));
    //        return (M)proxy;
    //    }

    //    //internal static void MutateProperty(M mutableInstance, string property, object value)
    //    //{

    //    //}

    //    internal class MutableInterceptor : IInterceptor //<M> : IInterceptor where M : class, IModl
    //    {
    //        private IMutable immutableInstance;
    //        private Dictionary<string, IProperty> mutatedValues = new Dictionary<string, IProperty>();

    //        public MutableInterceptor(IMutable immutableInstance)
    //        {
    //            this.immutableInstance = immutableInstance;

    //            //if (this.immutableInstance.Modl.Backer.IsNew && 
    //            //    immutableInstance.Modl.Backer.Definitions.HasIdProperty && 
    //            //    immutableInstance.Modl.Backer.Definitions.IdProperty (Id.IsAutomatic || Id.IsSet))

    //        }

    //        public void Intercept(IInvocation invocation)
    //        {
    //            var info = new InvocationInfo(invocation);

    //            if (info.CallType == CallType.Set)
    //            {
    //                //if (!immutableInstance.Modl.Backer.Definitions.Properties.Any(x => x.PropertyName == info.Property))
    //                //    throw new InvalidPropertyNameException($"Property with name '{info.Property}' doesn't exist on type '{immutableInstance.GetType()}'");

    //                var definition = immutableInstance.Modl.Backer.Definitions.Properties.SingleOrDefault(x => x.PropertyName == info.Property);
    //                if (definition == null)
    //                    throw new InvalidPropertyNameException($"Property with name '{info.Property}' doesn't exist on type '{immutableInstance.GetType()}'");

    //                if (definition.IsLink)
    //                    mutatedValues[info.Property] = new RelationProperty(definition, info.Property, info.Value as IModl);
    //                else
    //                    mutatedValues[info.Property] = new SimpleProperty(definition, info.Value);
    //            }
    //            else
    //            {
    //                if (info.Property == "Modl")
    //                    invocation.ReturnValue = immutableInstance.Modl;
    //                else if (info.Property == "IsMutable")
    //                    invocation.ReturnValue = true;
    //                else if (info.Property == "IsNew")
    //                    invocation.ReturnValue = immutableInstance.Modl.Backer.IsNew;
    //                else if (info.Property == "IsModified")
    //                    invocation.ReturnValue = mutatedValues.Any();
    //                else if (info.Property == "GetChanges")
    //                    invocation.ReturnValue = new ChangeCollection(GetChanges());
    //                else
    //                {
    //                    var definition = immutableInstance.Modl.Backer.Definitions.Properties.SingleOrDefault(x => x.PropertyName == info.Property);
    //                    if (definition == null)
    //                        throw new InvalidPropertyNameException($"Property with name '{info.Property}' doesn't exist on type '{immutableInstance.GetType()}'");

    //                    if (definition.IsLink)
    //                    {
    //                        if (mutatedValues.ContainsKey(info.Property))
    //                            invocation.ReturnValue = (mutatedValues[info.Property] as IRelationProperty).Value;
    //                        else
    //                            invocation.ReturnValue = immutableInstance.Modl.Backer.RelationValueBacker.GetValue(info.Property).Get();
    //                    }
    //                    else
    //                    {
    //                        if (mutatedValues.ContainsKey(info.Property))
    //                            invocation.ReturnValue = (mutatedValues[info.Property] as ISimpleProperty).Value;
    //                        else
    //                            invocation.ReturnValue = immutableInstance.Modl.Backer.SimpleValueBacker.GetValue(info.Property).Get();
    //                    }
    //                }
    //            }
    //        }


            

    //        private IEnumerable<Change> GetChanges()
    //        {
    //            if (immutableInstance.IsNew)
    //                yield return new Change(Guid.NewGuid(), immutableInstance, null, null, ChangeType.Created);

    //            if (immutableInstance.Modl.Backer.IsNew && immutableInstance.Modl.Backer.Definitions.HasAutomaticId)
    //                yield return new Change(Guid.NewGuid(), immutableInstance, null, new SimpleProperty(immutableInstance.Modl.Backer.Definitions.IdProperty, immutableInstance.Modl.Id.Get()), ChangeType.Value);

    //            foreach (var mutatedValue in mutatedValues)
    //            {
    //                var newProperty = mutatedValue.Value;
    //                var oldProperty = newProperty.Metadata.IsLink 
    //                    ? new RelationProperty(newProperty.Metadata, mutatedValue.Key, immutableInstance.Modl.Backer.RelationValueBacker.GetValue(mutatedValue.Key).Get() as IModl) as IProperty
    //                    : new SimpleProperty(newProperty.Metadata, immutableInstance.Modl.Backer.SimpleValueBacker.GetValue(mutatedValue.Key).Get());
                    
    //                yield return new Change(Guid.NewGuid(), immutableInstance, oldProperty, newProperty, ChangeType.Value);
    //            }
    //        }
    //    }
    //}

    internal enum CallType
    {
        Get,
        Set
    }

    internal enum MethodType
    {
        Property,
        Indexer,
        Changes
    }

    internal struct InvocationInfo
    {
        

        internal CallType CallType { get; }
        internal MethodType MethodType { get; }
        internal string Property { get; }
        internal object Value { get; }

        internal InvocationInfo(IInvocation invocation)
        {
            var name = invocation.Method.Name;

            if (name.StartsWith("set_", StringComparison.Ordinal))
                this.CallType = CallType.Set;
            else if (name.StartsWith("get_", StringComparison.Ordinal))
                this.CallType = CallType.Get;
            else if (name == "GetChanges")
                this.CallType = CallType.Get;
            else
                throw new NotImplementedException();

            if (this.CallType == CallType.Get && name == "GetChanges")
            {
                this.MethodType = MethodType.Property;
                this.Property = name;
            }
            else if ((this.CallType == CallType.Get && invocation.Arguments.Length == 1) || (this.CallType == CallType.Set && invocation.Arguments.Length == 2))
            {
                this.MethodType = MethodType.Indexer;
                this.Property = invocation.Arguments[0] as string;
            }
            else
            {
                this.MethodType = MethodType.Property;
                this.Property = name.Substring(4);
            }

            if (this.CallType == CallType.Set && this.MethodType == MethodType.Property)
                this.Value = invocation.Arguments[0];
            else if (this.CallType == CallType.Set && this.MethodType == MethodType.Indexer)
                this.Value = invocation.Arguments[1];
            else
                this.Value = null;
        }
    }

    //public class DelegateWrapper
    //{
    //    public static T WrapAs<T>(Delegate impl)// where T : class
    //    {
    //        var generator = new ProxyGenerator();
    //        var proxy = generator.CreateInterfaceProxyWithoutTarget((typeof(T), new PropertyInterceptor(impl));
    //        return (T)proxy;
    //    }
    //}
}
