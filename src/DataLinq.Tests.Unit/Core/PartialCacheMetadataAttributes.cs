using DataLinq.Attributes;

namespace DataLinq.Tests.Unit.Core;

// These declarations deliberately have no model interfaces or base lists.
[UseCache]
public partial class PartialCachedDb { }

[UseCache(false)]
public partial class PartialUncachedDb { }

[UseCache(false)]
public abstract partial class PartialOptOutRow { }

[UseCache(true)]
public abstract partial class PartialOptInRow { }
