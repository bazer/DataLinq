using System;
using DataLinq;
using DataLinq.Interfaces;
using DataLinq.Attributes;
using DataLinq.Instances;
using DataLinq.Mutation;

namespace DataLinq.Tests.Models.Allround;

[UseCache]
[Database("AllroundBenchmark")]
public partial class AllroundBenchmark(DataSourceAccess dataSource) : IDatabaseModel
{
    public DbRead<Discount> Discounts { get; } = new DbRead<Discount>(dataSource);
    public DbRead<Inventory> Inventory { get; } = new DbRead<Inventory>(dataSource);
    public DbRead<Location> Locations { get; } = new DbRead<Location>(dataSource);
    public DbRead<Locationhistory> Locationshistory { get; } = new DbRead<Locationhistory>(dataSource);
    public DbRead<Manufacturer> Manufacturers { get; } = new DbRead<Manufacturer>(dataSource);
    public DbRead<Orderdetail> Orderdetails { get; } = new DbRead<Orderdetail>(dataSource);
    public DbRead<Order> Orders { get; } = new DbRead<Order>(dataSource);
    public DbRead<Payment> Payments { get; } = new DbRead<Payment>(dataSource);
    public DbRead<Productcategory> Productcategories { get; } = new DbRead<Productcategory>(dataSource);
    public DbRead<Productimage> Productimages { get; } = new DbRead<Productimage>(dataSource);
    public DbRead<Productreview> Productreviews { get; } = new DbRead<Productreview>(dataSource);
    public DbRead<Product> Products { get; } = new DbRead<Product>(dataSource);
    public DbRead<Producttag> Producttags { get; } = new DbRead<Producttag>(dataSource);
    public DbRead<Shippingcompany> Shippingcompanies { get; } = new DbRead<Shippingcompany>(dataSource);
    public DbRead<Usercontact> Usercontacts { get; } = new DbRead<Usercontact>(dataSource);
    public DbRead<Userfeedback> Userfeedback { get; } = new DbRead<Userfeedback>(dataSource);
    public DbRead<Userhistory> Userhistory { get; } = new DbRead<Userhistory>(dataSource);
    public DbRead<Userprofile> Userprofiles { get; } = new DbRead<Userprofile>(dataSource);
    public DbRead<User> Users { get; } = new DbRead<User>(dataSource);
}