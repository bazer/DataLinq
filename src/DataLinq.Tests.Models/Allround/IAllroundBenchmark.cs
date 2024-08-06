using System;
using DataLinq;
using DataLinq.Interfaces;
using DataLinq.Attributes;

namespace DataLinq.Tests.Models.Allround;

[UseCache]
[Database("AllroundBenchmark")]
public interface IAllroundBenchmark : IDatabaseModel
{
    DbRead<IDiscount> Discounts { get; }
    DbRead<IInventory> Inventory { get; }
    DbRead<ILocation> Locations { get; }
    DbRead<ILocationhistory> Locationshistory { get; }
    DbRead<IManufacturer> Manufacturers { get; }
    DbRead<IOrderdetail> Orderdetails { get; }
    DbRead<IOrder> Orders { get; }
    DbRead<IPayment> Payments { get; }
    DbRead<IProductcategory> Productcategories { get; }
    DbRead<IProductimage> Productimages { get; }
    DbRead<IProductreview> Productreviews { get; }
    DbRead<IProduct> Products { get; }
    DbRead<IProducttag> Producttags { get; }
    DbRead<IShippingcompany> Shippingcompanies { get; }
    DbRead<IUsercontact> Usercontacts { get; }
    DbRead<IUserfeedback> Userfeedback { get; }
    DbRead<IUserhistory> Userhistory { get; }
    DbRead<IUserprofile> Userprofiles { get; }
    DbRead<IUser> Users { get; }
}