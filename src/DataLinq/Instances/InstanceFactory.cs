using System;
using System.Collections.Generic;
using Castle.DynamicProxy;
using DataLinq.Interfaces;
using DataLinq.Metadata;
using DataLinq.Mutation;

namespace DataLinq.Instances
{
    public interface InstanceBase
    {
        //bool IsNewModel { get; }
    }

    public interface ImmutableInstanceBase : InstanceBase
    {
        object Mutate();
    }

    public interface MutableInstanceBase : InstanceBase
    {
        IEnumerable<KeyValuePair<Column, object>> GetChanges();
    }

    public static class InstanceFactory
    {
        private static readonly ProxyGenerator generator = new();
        private static readonly ProxyGenerationOptions options = new ProxyGenerationOptions(new RowInterceptorGenerationHook());

        public static object NewImmutableRow(RowData rowData, IDatabaseProvider databaseProvider, Transaction transaction) // where T : class, IModel
        {
            object row;

            if (rowData.Table.Model.CsType.IsInterface)
                row = generator.CreateInterfaceProxyWithoutTarget(rowData.Table.Model.CsType, new Type[] { typeof(ImmutableInstanceBase) }, options, new ImmutableRowInterceptor(rowData, databaseProvider, transaction));
            else
                row = generator.CreateClassProxy(rowData.Table.Model.CsType, new Type[] { typeof(ImmutableInstanceBase) }, options, new ImmutableRowInterceptor(rowData, databaseProvider, transaction));

            //if (rowData.Table.Model.ProxyType == null)
            //    rowData.Table.Model.ProxyType = row.GetType();

            return row;
            //return generator.CreateInterfaceProxyWithoutTarget(instanceData.Table.Model.CsType, new ImmutableRowInterceptor(instanceData));
            //return generator.CreateInterfaceProxyWithoutTarget<T>(new ImmutableRowInterceptor(instanceData));
        }

        public static object NewMutableRow(RowData rowData, IDatabaseProvider databaseProvider, Transaction? transaction) // where T : class, IModel
        {
            object row;
            if (rowData.Table.Model.CsType.IsInterface)
                row = generator.CreateInterfaceProxyWithoutTarget(rowData.Table.Model.CsType,
                    new Type[] { typeof(MutableInstanceBase) }, options,
                    new MutableRowInterceptor(rowData, databaseProvider, transaction));
            else
                row = generator.CreateClassProxy(rowData.Table.Model.CsType,
                    new Type[] { typeof(MutableInstanceBase) }, options,
                    new MutableRowInterceptor(rowData, databaseProvider, transaction));

            //if (rowData.Table.Model.MutableProxyType == null)
            //    rowData.Table.Model.MutableProxyType = row.GetType();

            return row;
        }

        public static T NewDatabase<T>(Transaction transaction) where T : class, IDatabaseModel
        {
            return generator.CreateInterfaceProxyWithoutTarget<T>(new DatabaseInterceptor(transaction));
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