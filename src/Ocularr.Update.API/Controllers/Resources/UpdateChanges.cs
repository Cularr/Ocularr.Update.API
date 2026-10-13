using System.Collections.Generic;

namespace Ocularr.Update.API.Controllers.Update
{
    public class UpdateChanges
    {
        public List<string> New { get; set; } = new List<string>();

        public List<string> Fixed { get; set; } = new List<string>();
    }
}
