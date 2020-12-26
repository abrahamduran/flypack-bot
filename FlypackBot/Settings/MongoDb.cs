namespace FlypackBot.Settings
{
    public class MongoDb
    {
        public string Username { get; set; }
        public string Password { get; set; }
        public string Server { get; set; }
        public string DatabaseName { get; set; }
        public int Port { get; set; }

        public string ConnectionString
            => $"mongodb://{Username}:{Password}@{Server}:{Port}/{DatabaseName}";
    }
}
