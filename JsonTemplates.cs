﻿namespace PhValheimCompanion
{
    public class JsonTemplates
    {

        public class PhValheimBackendPost_serverStarted
        {
            public string action { get; set; }
            public string world { get; set; }
        }

        public class PhValheimBackendPost_playerJoined
        {
            public string action { get; set; }
            public string world { get; set; }
            public string citizen { get; set; }
            public string steamid { get; set; }
            public uint clientip { get; set; }
        }

        public class PhValheimBackendPost_headHung
        {
            public string action { get; set; }
            public string world { get; set; }
        }


    }
}