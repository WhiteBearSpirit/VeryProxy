using System;

namespace VeryProxy
{
    public static class Log
    {
        static string timestamp
        {
            get { return ("[" + DateTime.Now.ToString("HH:mm:ss") + "] "); }
        }

        
        public static void Event(object o)
        {
            Line("   **** "+ o + " ****");
        }

        public static void Data(object o)
        {
            Line(o.GetType() + " Data:" + Environment.NewLine);
            Console.Write(o.ToString() + Environment.NewLine);
        }
        

        public static void Line()
        {
            Console.Write(Environment.NewLine + timestamp); 
        }
        public static void Line(object o, bool withTimestamp = true)
        {
            Console.Write(Environment.NewLine + 
                (withTimestamp ? timestamp : "") + 
                o.ToString().Replace('\r', ' ').Replace('\n', ' '));            
        }

        public static void SameLine(object o)
        {
            Console.Write(o.ToString().Replace('\r', ' ').Replace('\n', ' '));
        }
    }
}
