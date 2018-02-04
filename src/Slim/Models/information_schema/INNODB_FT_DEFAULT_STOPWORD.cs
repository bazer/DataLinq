using System;
using Slim.Interfaces;
using Slim.Attributes;

namespace Slim.Models
{
    public interface INNODB_FT_DEFAULT_STOPWORD : IViewModel
    {
        [Type("varchar", 18)]
        string value { get; }

    }
}