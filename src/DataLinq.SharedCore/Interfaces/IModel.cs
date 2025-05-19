namespace DataLinq.Interfaces;

public interface IModel
{

}

public interface IModel<T> : IModel
    where T : IDatabaseModel
{

}

public interface IDatabaseModel
{

}

public interface ITableModel : IModel
{

}

public interface ITableModel<T> : IModel<T>
    where T : IDatabaseModel
{

}

public interface IViewModel : IModel
{

}

public interface IViewModel<T> : IModel<T>
    where T : IDatabaseModel
{

}
