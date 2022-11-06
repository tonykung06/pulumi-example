using System;
using System.Globalization;
using System.Linq;
using Microsoft.Extensions.Logging;
using Pulumi;
using Pulumi.Gcp.CloudFunctions;
using Pulumi.Gcp.Storage;

class SignupFunction
{
    public SignupFunction()
    {
        var database = new Database();

        var functionCodeBucket = new Bucket("funciton-code", new BucketArgs
        {
            Location = "US"
        });
        var cloudFunctionFileArchive = new FileArchive("../backend/handleSignup");
        var functionCodeObject = new BucketObject("function-code", new BucketObjectArgs
        {
            Bucket = functionCodeBucket.Name,
            Source = cloudFunctionFileArchive,
            Name = "source",
            CacheControl = "public, max-age=0"
        });
        var serviceAccount = new Pulumi.Gcp.ServiceAccount.Account("handle-signup-account", new Pulumi.Gcp.ServiceAccount.AccountArgs
        {
            AccountId = $"handle-signup-account-{Deployment.Instance.StackName}"
        });

        // when cloud function code content is changed,
        // the gcp cloud build is not triggered to redeploy the new code to the existing cloud function
        // so work around by deploy new cloud function anytime the code is updated.
        var sanitizedHash = functionCodeObject.Md5hash.Apply(x => string.Join("", Convert.FromBase64String(x).Select(x => $"{x:x2}")));

        var function = new Function("handle-signup", new FunctionArgs
        {
            Name = sanitizedHash.Apply(hash => $"handle-signup-{Deployment.Instance.StackName}-{hash}"),
            EntryPoint = "handleSignup",
            SourceArchiveBucket = functionCodeBucket.Name,
            SourceArchiveObject = functionCodeObject.OutputName,
            Runtime = "nodejs10",
            Region = "us-central1",
            TriggerHttp = true,
            ServiceAccountEmail = serviceAccount.Email,
            EnvironmentVariables = new InputMap<object>
            {
                ["DB_USER"] = database.DbUsername,
                ["DB_PASS"] = database.DbPassword,
                ["DB_NAME"] = database.DatabaseName,
                ["CLOUD_SQL_CONNECTION_NAME"] = database.InstanceConnectionName
            }
        });

        new FunctionIamBinding($"allow-all", new FunctionIamBindingArgs
        {
            Members = "allUsers",
            Role = "roles/cloudfunctions.invoker",
            CloudFunction = function.Name,
            Region = "us-central1"
        });

        var memberId = serviceAccount.Email.Apply(x => $"serviceAccount:{x}");

        new FunctionIamBinding($"service-agent", new FunctionIamBindingArgs
        {
            Members = memberId,
            Role = "roles/cloudfunctions.serviceAgent",
            CloudFunction = function.Name,
            Region = "us-central1"
        });

        // Projects.IAMBinding is a stateful object in which the members could be changed by another stack/env
        // this is a per role setting in a project
        // so create a custom role for every stack
        var myCustomRoleCloudSqlClient = new Pulumi.Gcp.Projects.IAMCustomRole($"cloudsql-client-{Deployment.Instance.StackName}", new()
        {
            Description = "Cloudsql Client for pulumi stack",
            Permissions = new[]
            {
                "cloudsql.instances.connect",
                "cloudsql.instances.get"
            },
            RoleId = $"cloudsqlClient{CultureInfo.CurrentCulture.TextInfo.ToTitleCase(Deployment.Instance.StackName)}",
            Title = $"cloudsql-client-{Deployment.Instance.StackName}",
        });
        new Pulumi.Gcp.Projects.IAMBinding($"service-agent-cloudsql-client", new Pulumi.Gcp.Projects.IAMBindingArgs
        {
            Members = memberId,
            Role = myCustomRoleCloudSqlClient.Id,
            Project = "pulumi-trial-367514"
        });
        FunctionUrl = function.HttpsTriggerUrl;
    }

    public Output<string> FunctionUrl { get; }
}