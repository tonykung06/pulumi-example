using Pulumi;
using Pulumi.Gcp.Sql;
using Pulumi.Gcp.Sql.Inputs;
using Pulumi.Random;

class Database
{
    public Output<string> InstanceConnectionName { get; }
    public Output<string> DbUsername { get; }

    public Output<string> DbPassword { get; }

    public Output<string> DatabaseName { get; }
    public Database()
    {
        var config = new Pulumi.Config();
        var dbServer = new DatabaseInstance("db-server", new DatabaseInstanceArgs
        {
            DatabaseVersion = "POSTGRES_13",
            DeletionProtection = Deployment.Instance.StackName == "prod",
            Settings = new DatabaseInstanceSettingsArgs
            {
                Tier = config.Require("databaseTier"), // config.Get("databaseTier") ?? "db-f1-micro"
                IpConfiguration = new DatabaseInstanceSettingsIpConfigurationArgs
                {
                    AuthorizedNetworks = new[] {
                        new DatabaseInstanceSettingsIpConfigurationAuthorizedNetworkArgs{
                            Name = "my internet",
                            Value = "221.127.5.130/32"
                        }
                    }
                }
            },
            Region = "us-central1"
        });

        var password = new RandomPassword("dbuser-password", new RandomPasswordArgs
        {
            Length = 32
        });

        var user = new User("cloudfunction-submitter", new UserArgs
        {
            Name = "cloudfunction-submitter",
            Password = password.Result,
            Instance = dbServer.Name
        });

        var db = new Pulumi.PostgreSql.Database("db", new Pulumi.PostgreSql.DatabaseArgs
        {
            Name = "submission",
            Owner = user.Name
        }, new CustomResourceOptions
        {
            Provider = new Pulumi.PostgreSql.Provider("pgProvider", new Pulumi.PostgreSql.ProviderArgs
            {
                Host = dbServer.PublicIpAddress,
                Username = user.Name,
                Password = password.Result
            })
        });

        InstanceConnectionName = dbServer.ConnectionName;
        DbUsername = user.Name;
        DbPassword = password.Result;
        DatabaseName = db.Name;
    }
}