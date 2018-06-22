using System;
using System.Collections.Generic;
using System.Text;

namespace DotaSpamBot
{
    public class ConfigModel
    {
        public string login { get; set; }
        public string password { get; set; }
        public string msgText { get; set; }
        public int minChannelUsersCount { get; set; }
    }
}
