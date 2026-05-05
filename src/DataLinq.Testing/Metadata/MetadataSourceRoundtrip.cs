using System;
using System.IO;
using System.Linq;
using DataLinq.Core.Factories.Models;
using DataLinq.Metadata;
using ThrowAway.Extensions;

namespace DataLinq.Testing;

public static class MetadataSourceRoundtrip
{
    public static DatabaseDefinition ParseGeneratedModelSource(DatabaseDefinition metadata)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"datalinq-generated-metadata-{Guid.NewGuid():N}");

        try
        {
            Directory.CreateDirectory(directory);

            var modelFileFactory = new ModelFileFactory(new ModelFileFactoryOptions
            {
                UseNullableReferenceTypes = true
            });

            foreach (var file in modelFileFactory.CreateModelFiles(metadata))
            {
                var filePath = Path.Combine(directory, file.path);
                Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
                File.WriteAllText(filePath, file.contents);
            }

            return new MetadataFromFileFactory(new MetadataFromFileFactoryOptions())
                .ReadFiles(metadata.CsType.Name, [directory])
                .ValueOrException()
                .Single(database => database.CsType.Name == metadata.CsType.Name);
        }
        finally
        {
            try
            {
                if (Directory.Exists(directory))
                    Directory.Delete(directory, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
