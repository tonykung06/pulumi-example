using Pulumi;
using Pulumi.Gcp.CloudFunctions;
using Pulumi.Gcp.Storage;
using System.Collections.Generic;
using System.IO;

return await Deployment.RunAsync(() =>
{
    var signupFunction = new SignupFunction();

    // Create a GCP resource (Storage Bucket)
    var bucket = new Bucket("frontend-files", new BucketArgs
    {
        Location = "US"
    });

    var frontendConfigJson = signupFunction.FunctionUrl.Apply(url => $@"{{
        ""backendUrl"": ""{url}""
    }}");
    var configBucketObject = new BucketObject("config.json", new BucketObjectArgs
    {
        Bucket = bucket.Name,
        Content = frontendConfigJson,
        Name = "config.json",
        CacheControl = "public, max-age=0"
    });

    var frontendBuildOutputFolder = "../frontend/build";
    var files = Directory.GetFiles(frontendBuildOutputFolder, "*", SearchOption.AllDirectories);

    foreach (var file in files)
    {
        var fileName = Path.GetRelativePath(frontendBuildOutputFolder, file).Replace("\\", "/");
        new BucketObject(fileName, new BucketObjectArgs
        {
            Bucket = bucket.Name,
            Source = new FileAsset(file),
            Name = fileName,
            ContentType = fileName.EndsWith(".css") ? "text/css; charset=utf-8" : "",
            CacheControl = "public, max-age=0"
        });
    }

    var allowAllBinding = new BucketIAMBinding("allowAll", new BucketIAMBindingArgs
    {
        Bucket = bucket.Name,
        Role = "roles/storage.objectViewer",
        Members = new[] { "allUsers" }
    });

    var dnsHosting = new DnsHosting(bucket.Name);

    // Export the DNS name of the bucket
    return new Dictionary<string, object?>
    {
        ["bucketName"] = bucket.Url
    };
});
