﻿using System;
using System.Collections.Generic;
using System.Text;

namespace Slim.Metadata
{
    public class Model
    {
        public Type CsType { get; set; }
        public Type ProxyType { get; set; }
        public Type MutableProxyType { get; set; }
        public string CsTypeName { get; set; }
        public DatabaseMetadata Database { get; set; }
        public List<Property> Properties { get; set; }
        public object[] Attributes { get; set; }
    }
}
