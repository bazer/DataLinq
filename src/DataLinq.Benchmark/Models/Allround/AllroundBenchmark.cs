using System;
using DataLinq.Interfaces;
using DataLinq.Attributes;

namespace DataLinq.Benchmark.Models.Allround;

[UseCache]
[Database("AllroundBenchmark")]
public interface AllroundBenchmark : IDatabaseModel
{
    DbRead<Discount> Discounts { get; }
    DbRead<Inventory> Inventory { get; }
    DbRead<Location> Locations { get; }
    DbRead<Locationhistory> Locationshistory { get; }
    DbRead<Manufacturer> Manufacturers { get; }
    DbRead<Orderdetail> Orderdetails { get; }
    DbRead<Order> Orders { get; }
    DbRead<Payment> Payments { get; }
    DbRead<Productcategory> Productcategories { get; }
    DbRead<Productimage> Productimages { get; }
    DbRead<Productreview> Productreviews { get; }
    DbRead<Product> Products { get; }
    DbRead<Producttag> Producttags { get; }
    DbRead<Shippingcompany> Shippingcompanies { get; }
    DbRead<Usercontact> Usercontacts { get; }
    DbRead<Userfeedback> Userfeedback { get; }
    DbRead<Userhistory> Userhistory { get; }
    DbRead<Userprofile> Userprofiles { get; }
    DbRead<User> Users { get; }
}