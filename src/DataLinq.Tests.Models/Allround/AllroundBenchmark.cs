using System;
using DataLinq;
using DataLinq.Interfaces;
using DataLinq.Attributes;

namespace DataLinq.Tests.Models.Allround;

[UseCache]
[Database("AllroundBenchmark")]
public partial class AllroundBenchmark(IDataLinqReadSource readSource) : IDatabaseModel
{
    public DbRead<Discount> Discounts { get; } = new(readSource);
    public DbRead<Inventory> Inventory { get; } = new(readSource);
    public DbRead<Location> Locations { get; } = new(readSource);
    public DbRead<Locationhistory> Locationshistory { get; } = new(readSource);
    public DbRead<Manufacturer> Manufacturers { get; } = new(readSource);
    public DbRead<Orderdetail> Orderdetails { get; } = new(readSource);
    public DbRead<Order> Orders { get; } = new(readSource);
    public DbRead<Payment> Payments { get; } = new(readSource);
    public DbRead<Productcategory> Productcategories { get; } = new(readSource);
    public DbRead<Productimage> Productimages { get; } = new(readSource);
    public DbRead<Productreview> Productreviews { get; } = new(readSource);
    public DbRead<Product> Products { get; } = new(readSource);
    public DbRead<Producttag> Producttags { get; } = new(readSource);
    public DbRead<Shippingcompany> Shippingcompanies { get; } = new(readSource);
    public DbRead<Usercontact> Usercontacts { get; } = new(readSource);
    public DbRead<Userfeedback> Userfeedback { get; } = new(readSource);
    public DbRead<Userhistory> Userhistory { get; } = new(readSource);
    public DbRead<Userprofile> Userprofiles { get; } = new(readSource);
    public DbRead<User> Users { get; } = new(readSource);
}
