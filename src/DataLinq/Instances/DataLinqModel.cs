﻿using System;
using System.Collections.Generic;
using System.Text;

namespace DataLinq.Instances
{
    public class DataLinqModel<T>
    {
        public DataLinqModel(T model)
        {
            Model = model;
        }

        //public bool IsNew()
        //{
        //    return true;
        //}

        public T Model { get; }
    }
}
