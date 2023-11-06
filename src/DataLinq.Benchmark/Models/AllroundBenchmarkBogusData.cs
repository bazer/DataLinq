using Bogus;
using DataLinq.Benchmark.Models.Allround;

namespace DataLinq.Benchmark.Models;

internal static class AllroundBenchmarkBogusData
{
    public static void FillAllroundBenchmarkWithBogusData(Database<AllroundBenchmark> db, decimal numMillionRows = 0.01m)
    {
        var lockObject = new object();
        lock (lockObject)
        {
            Randomizer.Seed = new Random(59345922);
            using var transaction = db.Transaction();

            // Generate data for Users table
            var userFaker = new Faker<User>()
                .RuleFor(u => u.UserId, f => f.Random.Uuid())
                .RuleFor(u => u.UserName, f => f.Internet.UserName())
                .RuleFor(u => u.Email, f => f.Internet.Email())
                .RuleFor(u => u.DateJoined, f => f.Date.PastDateOnly(3))
                .RuleFor(u => u.UserRole, f => f.PickRandom<User.UserRoleValue>());

            var users = transaction.Insert(userFaker.Generate((int)(200000 * numMillionRows)));
            var userIds = users.Select(u => u.UserId).ToList();

            // Generate data for Products table
            var productFaker = new Faker<Product>()
                .RuleFor(p => p.ProductId, f => f.Random.Uuid())
                .RuleFor(p => p.ProductName, f => f.Commerce.ProductName())
                .RuleFor(p => p.Price, f => f.Random.Decimal(1, 10000));

            var products = transaction.Insert(productFaker.Generate((int)(200000 * numMillionRows)));
            var productIds = products.Select(p => p.ProductId).ToList();

            // Generate data for Locations table
            var locationFaker = new Faker<Location>()
                .RuleFor(l => l.LocationId, f => f.Random.Uuid())
                .RuleFor(l => l.Address, f => f.Address.StreetAddress())
                .RuleFor(l => l.City, f => f.Address.City())
                .RuleFor(l => l.Country, f => f.Address.Country());

            var locations = transaction.Insert(locationFaker.Generate((int)(50000 * numMillionRows)));
            var locationIds = locations.Select(l => l.LocationId).ToList();

            // Generate data for Manufacturers table
            var manufacturerFaker = new Faker<Manufacturer>()
                //.RuleFor(m => m.ManufacturerId, f => f.IndexFaker)
                .RuleFor(m => m.ManufacturerName, f => f.Company.CompanyName());

            var manufacturers = transaction.Insert(manufacturerFaker.Generate((int)(1000 * numMillionRows)));
            var manufacturerIds = manufacturers.Select(m => m.ManufacturerId).ToList();

            // Generate data for ProductCategories table
            var productCategoryFaker = new Faker<Productcategory>()
                .RuleFor(pc => pc.CategoryId, f => f.Random.Uuid())
                .RuleFor(pc => pc.CategoryName, f => f.Commerce.Categories(1).First());

            var productCategories = transaction.Insert(productCategoryFaker.Generate((int)(100 * numMillionRows)));
            var categoryIds = productCategories.Select(pc => pc.CategoryId).ToList();


            // Generate data for ShippingCompanies table
            var shippingCompanyFaker = new Faker<Shippingcompany>()
                //.RuleFor(sc => sc.ShippingCompanyId, f => f.IndexFaker)
                .RuleFor(sc => sc.CompanyName, f => f.Company.CompanyName());

            var shippingCompanies = transaction.Insert(shippingCompanyFaker.Generate((int)(100 * numMillionRows)));
            var shippingCompanyIds = shippingCompanies.Select(sc => sc.ShippingCompanyId).ToList();

            // Generate data for Orders table
            var orderFaker = new Faker<Order>()
                .RuleFor(o => o.OrderId, f => f.Random.Uuid())
                .RuleFor(o => o.ProductId, f => f.PickRandom(productIds))
                .RuleFor(o => o.UserId, f => f.PickRandom(userIds))
                .RuleFor(o => o.OrderDate, f => f.Date.PastDateOnly(1))
                .RuleFor(o => o.OrderStatus, f => f.PickRandom<Order.OrderStatusValue>())
                .RuleFor(o => o.ShippingCompanyId, f => f.PickRandom(shippingCompanyIds))
                .RuleFor(o => o.OrderTimestamp, f => f.Date.Past(1));

            var orders = transaction.Insert(orderFaker.Generate((int)(150000 * numMillionRows)));
            var orderIds = orders.Select(o => o.OrderId).ToList();

            // Generate data for Payments table
            var paymentFaker = new Faker<Payment>()
                //.RuleFor(p => p.PaymentId, f => f.IndexFaker)
                .RuleFor(p => p.OrderId, f => f.PickRandom(orderIds))
                .RuleFor(p => p.Amount, f => f.Random.Decimal(1, 10000))
                .RuleFor(p => p.PaymentDate, f => f.Date.PastDateOnly(1))
                .RuleFor(p => p.PaymentMethod, f => f.PickRandom<Payment.PaymentMethodValue>());

            var payments = transaction.Insert(paymentFaker.Generate((int)(150000 * numMillionRows)));

            // Generate data for UserProfiles table
            var userProfileFaker = new Faker<Userprofile>()
                .RuleFor(up => up.ProfileId, f => f.Random.Uuid())
                .RuleFor(up => up.UserId, f => f.PickRandom(userIds))
                .RuleFor(up => up.Bio, f => f.Lorem.Paragraph());

            var userProfiles = transaction.Insert(userProfileFaker.Generate((int)(200000 * numMillionRows)));

            // Generate data for UserContacts table
            var userContactFaker = new Faker<Usercontact>()
                //.RuleFor(uc => uc.ContactId, f => f.IndexFaker)
                .RuleFor(uc => uc.ProfileId, f => f.PickRandom(userProfiles.Select(up => up.ProfileId)))
                .RuleFor(uc => uc.Phone, f => f.Phone.PhoneNumber());

            var userContacts = transaction.Insert(userContactFaker.Generate((int)(200000 * numMillionRows)));

            // Generate data for UserFeedbacks table
            var userFeedbackFaker = new Faker<Userfeedback>()
                //.RuleFor(uf => uf.FeedbackId, f => f.IndexFaker)
                .RuleFor(uf => uf.ProductId, f => f.PickRandom(productIds))
                .RuleFor(uf => uf.UserId, f => f.PickRandom(userIds))
                .RuleFor(uf => uf.Feedback, f => f.Lorem.Sentence());

            var userFeedbacks = transaction.Insert(userFeedbackFaker.Generate((int)(150000 * numMillionRows)));

            // Generate data for UserHistories table
            var userHistoryFaker = new Faker<Userhistory>()
                //.RuleFor(uh => uh.HistoryId, f => f.IndexFaker)
                .RuleFor(uh => uh.UserId, f => f.PickRandom(userIds))
                .RuleFor(uh => uh.ActivityDate, f => f.Date.PastDateOnly(2))
                .RuleFor(uh => uh.ActivityLog, f => f.Lorem.Sentence());

            var userHistories = transaction.Insert(userHistoryFaker.Generate((int)(500000 * numMillionRows)));

            // Generate data for OrderDetails table
            var orderDetailFaker = new Faker<Orderdetail>()
                .RuleFor(od => od.DetailId, f => f.Random.Uuid())
                .RuleFor(od => od.OrderId, f => f.PickRandom(orderIds))
                .RuleFor(od => od.ProductId, f => f.PickRandom(productIds))
                .RuleFor(od => od.Quantity, f => f.Random.Int(1, 10));

            var orderDetails = transaction.Insert(orderDetailFaker.Generate((int)(50000 * numMillionRows)));


            // Generate data for Discounts table
            var discountFaker = new Faker<Discount>()
                //.RuleFor(d => d.DiscountId, f => f.IndexFaker)
                .RuleFor(d => d.ProductId, f => f.PickRandom(productIds))
                .RuleFor(d => d.DiscountPercentage, f => f.Random.Decimal(0.05m, 0.5m))  // assuming a percentage discount between 5% to 50%
                .RuleFor(d => d.StartDate, f => f.Date.PastDateOnly(1))
                .RuleFor(d => d.EndDate, f => f.Date.FutureDateOnly(1));

            var discounts = transaction.Insert(discountFaker.Generate((int)(50000 * numMillionRows)));

            // Generate data for Inventories table
            var inventoryFaker = new Faker<Inventory>()
                //.RuleFor(i => i.InventoryId, f => f.IndexFaker)
                .RuleFor(i => i.ProductId, f => f.PickRandom(productIds))
                .RuleFor(i => i.LocationId, f => f.PickRandom(locationIds))
                .RuleFor(i => i.Stock, f => f.Random.Int(1, 1000));

            var inventories = transaction.Insert(inventoryFaker.Generate((int)(200000 * numMillionRows)));

            // Generate data for LocationHistories table
            var locationHistoryFaker = new Faker<Locationhistory>()
                .RuleFor(lh => lh.HistoryId, f => f.Random.Uuid())
                .RuleFor(lh => lh.LocationId, f => f.PickRandom(locationIds))
                .RuleFor(lh => lh.ChangeDate, f => f.Date.PastDateOnly(2))
                .RuleFor(lh => lh.ChangeLog, f => f.Lorem.Sentence());

            var locationHistories = transaction.Insert(locationHistoryFaker.Generate((int)(500000 * numMillionRows)));

            // Generate data for ProductImages table
            var productImageFaker = new Faker<Productimage>()
                .RuleFor(pi => pi.ImageId, f => f.Random.Uuid())
                .RuleFor(pi => pi.ProductId, f => f.PickRandom(productIds))
                .RuleFor(pi => pi.ImageURL, f => f.Internet.Url());

            var productImages = transaction.Insert(productImageFaker.Generate((int)(250000 * numMillionRows)));

            // Generate data for ProductReviews table
            var productReviewFaker = new Faker<Productreview>()
                .RuleFor(pr => pr.ReviewId, f => f.Random.Uuid())
                .RuleFor(pr => pr.ProductId, f => f.PickRandom(productIds))
                .RuleFor(pr => pr.UserId, f => f.PickRandom(userIds))
                .RuleFor(pr => pr.Rating, f => f.Random.Int(1, 5))  // Assuming a 5-star rating system
                .RuleFor(pr => pr.Review, f => f.Lorem.Paragraph());

            var productReviews = transaction.Insert(productReviewFaker.Generate((int)(500000 * numMillionRows)));

            // Generate data for ProductTags table
            var productTagFaker = new Faker<Producttag>()
                //.RuleFor(pt => pt.TagId, f => f.IndexFaker)  // AutoIncremented
                .RuleFor(pt => pt.CategoryId, f => f.PickRandom(categoryIds))
                .RuleFor(pt => pt.TagName, f => f.Random.Word());

            var productTags = transaction.Insert(productTagFaker.Generate((int)(1000 * numMillionRows)));


            transaction.Commit();
        }
    }
}
