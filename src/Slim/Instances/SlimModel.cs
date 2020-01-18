using System;
using System.Collections.Generic;
using System.Text;

namespace Slim.Instances
{
    public class SlimModel<T>
    {
        public SlimModel(T model)
        {
            Model = model;
        }

        public bool IsNew()
        {
            return true;
        }

        public T Model { get; }
    }
}
