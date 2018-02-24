using System;
using Slim;
using Slim.Interfaces;
using Slim.Attributes;

namespace Slim.Models
{
    [Name("INNODB_FT_DEFAULT_STOPWORD")]
    public interface INNODB_FT_DEFAULT_STOPWORD : IViewModel
    {
        [Type("varchar", 18)]
        string value { get; }

    }
}