﻿using Newtonsoft.Json.Linq;

namespace SapphireDb.Command.Update
{
    public class UpdateCommand : CollectionCommandBase
    {
        public JObject Value { get; set; }
    }
}
