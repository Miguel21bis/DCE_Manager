/*
 * Denis Bekman 2009
 * www.youpvp.com/blog
 --
 * This code is licensed under a Creative Commons Attribution 3.0 United States License.
 * To view a copy of this license, visit http://creativecommons.org/licenses/by/3.0/us/
 * 
 * https://stackoverflow.com/questions/881445/easiest-way-to-parse-a-lua-datastructure-in-c-sharp-net
 * 
 */

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Globalization;
using DCE_Manager.Utils;
using DCE_Manager.Parameters;

namespace DCE_Manager
{
    public class LuaParse
    {
        
        List<string> toks = new List<string>();
        string LastToken = "";
        //Properties
        public string Id { get; set; }
        public LuaObject Val { get; set; }

        List<string> toks_a = new List<string>();

        // Constructor???
        public void Parse(string s)
        {
            //s = s.Replace("\'", "\"");        //ATTENTION a remettre si tout cassé

            string qs = string.Format("({0}[^{0}]*{0})", "\"");
            string[] z = Regex.Split(s, qs + @"(\]\]--)|(\]\])|(--\]\])|(--\[\[)|(=)|(,)|(\[)|(\])|(\{)|(\})|(--[^\n\r]*)");//| (--\[\[)

            //string[] z = Regex.Split(s, qs + @" \b(?!d, T\b)\w + (--\]\])| (--\[\[)|(=)|(,)|(\[)|(\])|(\{)|(\})|(--[^\n\r]*)");//| (--\[\[)

            Boolean flagCommentair = false;
            foreach (string toks in z)
            {
                if(toks.StartsWith("--[["))
                {
                    flagCommentair = true;
                }
 
                if (toks.Trim().Length != 0 && !toks.StartsWith("--") && flagCommentair == false)
                {
                    toks_a.Add(toks.Trim());
                }

                //if (tok.StartsWith("]]") || tok.StartsWith("]]--"))
                if (toks == "]]" || toks == "]]--" || toks == "--]]" )
                {
                    flagCommentair = false;
                }
            }

            //systeme pour eviter de garder "," au milieu d'une phrase 
            // si c'est entre guillement, on ajoute le "," au string
            // sinon, c'est considéré comme un tock à part entiere
            //livery = "3rd Main Jet Base Group Command, Turkey",
            bool openGuillemet = false;
            int firstGuillemet = 0;
            string betweenGuillement = "";
            for (int i = 0; i < toks_a.Count(); i++)
            {
                //var previous = i > 0 ? toks_a[i - 1] : null;
                var current = toks_a[i];
                //var next = i < toks_a.Count() ? toks_a[i + 1] : null;
                

                if(current.IndexOf("Campaign Briefing") > -1 )
                {

                }

                if (!openGuillemet && current.StartsWith("\"") && current.EndsWith("\"") )
                {
                    toks.Add(current.Trim());
                }
                else if (!openGuillemet && current.StartsWith("'") && current.EndsWith("'"))
                {
                    toks.Add(current.Trim());
                }
                else if (!openGuillemet && current.StartsWith("'") )
                {
                    betweenGuillement = betweenGuillement + current.Trim();
                    openGuillemet = true;
                    firstGuillemet = 1;
                }
                else if (!openGuillemet && current.StartsWith("\""))
                {
                    betweenGuillement = betweenGuillement + current.Trim();
                    openGuillemet = true;
                    firstGuillemet = 2;
                }

                //on estime que le premier guillemet ouvre le text, les suivants, non
                else if (openGuillemet && current.EndsWith("'") && firstGuillemet == 1)
                {
                    openGuillemet = false;
                    toks.Add(betweenGuillement + current.Trim());
                    firstGuillemet = 0;
                    betweenGuillement = "";
                }
                else if (openGuillemet && current.EndsWith("\"") && firstGuillemet == 2)
                {
                    openGuillemet = false;
                    toks.Add(betweenGuillement + current.Trim());
                    firstGuillemet = 0;
                    betweenGuillement = "";
                }




                //else if (current.StartsWith("\"") || (current.StartsWith("'")))
                //{
                //    openGuillemet = true;
                //    betweenGuillement = betweenGuillement + current.Trim();
                //}
                //else if (current.EndsWith("\"") || (current.EndsWith("'")))
                //{
                //    openGuillemet = false;
                //    toks.Add(betweenGuillement + current.Trim());
                //    betweenGuillement = "";
                //}
                else if (openGuillemet == false)
                {
                    toks.Add(current.Trim());
                }
                else if (openGuillemet )
                {
                    betweenGuillement = betweenGuillement + current.Trim();
                }
            }

            Assign();
        }
        protected void Assign()
        {
            if (!IsLiteral)
                throw new Exception("expect identifier");
            Id = GetToken();
            if (!IsToken("="))
                throw new Exception("expect '='");
  
            NextToken();
            Val = RVal();
        }
        protected LuaObject RVal()
        {
            if (IsToken("{"))
                return LuaObject();
            else if (IsString)
                return GetString();
            else if (IsNumber)
                return GetNumber();
            else if (IsFloat)
                return GetFloat();
            else if (IsBoolean)
                return GetBoolean();
            else if (IsNull)
                return GetNull();
            else if (IsLiteral)
                return GetLiteral();
            else if (IsToken(","))
                return GetString();
            else
            {
                string info = toks[0];
                throw new Exception("expecting '{', a string or a number");
            }
        }

        // Constructor
        protected LuaObject LuaObject()
        {
            Dictionary<string, LuaObject> table = new Dictionary<string, LuaObject>();

            Boolean maybeTable = false;

            //for (int nn = 0; nn < 10; nn++)
            //{
            //    if (nn <= toks.Count() - 1 && toks[nn].IndexOf("y") > -1)
            //    {
            //    }
            //}


            NextToken();

            while (!IsToken("}"))
            {

                //if (toks[0] == "Cyprus Border Force 4 T2.stm")
                //{

                //}
                //for (int nn = 0; nn < 10; nn++)
                //{
                //    if (nn <= toks.Count() - 1 && toks[nn].IndexOf("Cyprus Border Force 4 T2.stm") > -1)
                //    {
                //    }
                //}

                Boolean stringFlag = false;
                Boolean tokenFlag = false;
                Boolean inTable = false;

                string nextTokFString = "";
                string TokFirstTring = "";
                string TokLastTring = "";

                TokFirstTring = toks[0].Substring(0, 1);
                TokLastTring = toks[0][toks[0].Length - 1].ToString() ;
                if (toks.Count >= 2)
                    nextTokFString = toks[1].Substring(0, 1);

                if(maybeTable)
                {
                    if (TokLastTring == "'" && maybeTable)
                    {
                        maybeTable = false;  //  { 'Action.Text("The US has n PB avec parkAlertSAR = {{["y"] = 902622.66468491,
                     }
                }
                else
                {
                    if (IsToken("["))
                    {
                        if (nextTokFString == "'" || nextTokFString == "\"")
                        {
                            tokenFlag = true;    //  ["y"] = 70552.036482739,
                        }
                        else if (int.TryParse(nextTokFString, out int value))
                        {
                            tokenFlag = true;    //  [1] = 'Action.Tex....
                        }
                    }
                    //else if (IsToken("[") && (int.TryParse(nextTokFString, out int value)))
                    //{
                    //    tokenFlag = true;    //  [1] = 'Action.Tex....
                    //}
                    else if (!maybeTable && ((LastToken == "=" & TokFirstTring == "'") || (LastToken == "{" & TokFirstTring == "'") || (IsToken(",") & nextTokFString == "'")))
                    {
                        maybeTable = true;  //  { 'Action.Text("The US has n PB avec parkAlertSAR = {{["y"] = 902622.66468491,

                    }
                    else if (!maybeTable && TokFirstTring == "\"" && TokLastTring == "\"")
                    {
                        maybeTable = true;  //  livery = {"Cypriot Air Force 450th AHS Black Panther", "Cypriot National Guard - 819"},	
                    }
                    else if (!maybeTable && TokFirstTring == "'" && TokLastTring == "'")
                    {
                        maybeTable = true;  //  livery = {"Cypriot Air Force 450th AHS Black Panther", "Cypriot National Guard - 819"},	
                    }

                    else if (IsLiteral)
                        stringFlag = true;  //  x =	454116.78125,
                }



                if ( stringFlag)//&& !maybeTable
                {
                    string name = "";
                    
                    name = toks[0];

                    NextToken();

                    if (IsToken("="))
                    {
                        NextToken();
                    }            

                    var value = RVal();

                    if (!table.ContainsKey(name))
                    {
                        table.Add(name, value);
                    }
                    else
                    {
                        string msg = "0A Warning, this key |"
                         + name
                         + "| already exists, it's a duplicate. Review your tables in this file: "  + "\r\n"
                         + ParamCampaign.NameFileParse
                         + "\r\n" + "\r\n";

                        
                        FormUtils.LogRegister( msg);

                        //var form = Form.ActiveForm as PublicTable;
                        if (!PublicTable.errorTable.ContainsKey(name))
                        {
                            PublicTable.errorTable.Add(name, msg);
                        }

                    }


                }
                else if (tokenFlag)//&& !maybeTable
                {
                    NextToken();

                    string name = "";

                    if (IsString)
                        name = GetString();
                    else if (IsNumber)
                    {
                        int tempInt = GetNumber();
                        name = tempInt.ToString();
                    }

                    //NextToken(); ///ADD1

                    if ( !IsToken("]"))
                        throw new Exception("expecting ']'");

                    NextToken();

                    if (!IsToken("="))
                        throw new Exception("expecting '='");

                    NextToken();

                    var value = RVal();

                    if (!table.ContainsKey(name))
                    {
                        table.Add(name, value);
                    }
                    else
                    {
                        string msg = "0B Warning, this key |"
                         + name
                         + "| already exists, it's a duplicate. Review your tables in this file: "  + "\r\n"
                          + ParamCampaign.NameFileParse
                          + "\r\n" + "\r\n";

                        
                        FormUtils.LogRegister(msg);

                        if (!PublicTable.errorTable.ContainsKey(name))
                        {
                            PublicTable.errorTable.Add(name, msg);
                        }

                    }
                }
                else if (maybeTable)    //'Action.Text("The US has n
                {
                    inTable = true;
                   
                    string name = "";
                    
                    //var value = RVal();
                    var value = toks[0];
                    //toks.RemoveAt(0);//pas bien
                    NextToken();

                    name = name + "_" + table.Count;

                    if (!table.ContainsKey(name))
                    {
                        table.Add(name, value);
                    }
                    else
                    {
                        string msg = "0C Warning, this key |"
                         + name
                         + "| already exists, it's a duplicate. Review your tables in this file: "  + "\r\n"
                          + ParamCampaign.NameFileParse
                          + "\r\n" + "\r\n";

                       
                        FormUtils.LogRegister(msg);

                        if (!PublicTable.errorTable.ContainsKey(name))
                        {
                            PublicTable.errorTable.Add(name, msg);
                        }

                    }
                    TokLastTring = toks[0][toks[0].Length - 1].ToString();

                    if(TokLastTring == "'")
                    {
                        NextToken();
                    }

                    if (toks.Count >= 2)
                        nextTokFString = toks[1].Substring(0, 1);

                    if (IsToken(",") && nextTokFString != "}")
                    {
                        NextToken();
                    }
                    else if (IsToken(",") && nextTokFString == "}")
                    {
                        inTable = false;
                    }
                    else if (IsToken("}"))
                    {
                        inTable = false;
                        //NextToken();
                    }

                }
                else
                {
                   

                    if (!table.ContainsKey(table.Count.ToString()))
                    {
                        table.Add(table.Count.ToString(), RVal());//array
                    }
                    else
                        //MessageBox.Show("0D Warning, this key |"
                        //+ table.Count.ToString()
                        //+ "| already exists, it's a duplicate. Review your tables in this file: " + "\r\n" + "\r\n"
                        //+ Main_Form.ParamCampaign.NameFileParse + "\r\n"
                        //, "Info"
                        //);
                    {
                        string msg = "0D Warning, this key |"
                         + table.Count.ToString()
                         + "| already exists, it's a duplicate. Review your tables in this file: " + "\r\n"
                          + ParamCampaign.NameFileParse
                          + "\r\n" + "\r\n";

                        
                        FormUtils.LogRegister(msg);

                        if (!PublicTable.errorTable.ContainsKey(table.Count.ToString()))
                        {
                            PublicTable.errorTable.Add(table.Count.ToString(), msg);
                        }

                    }

                }

                if (!inTable)
                {
                    if (!IsToken(",") && !IsToken("}"))
                    {
                        //throw new Exception("expecting ','");
                        inTable = true;
                        maybeTable = true;
                    }

                    if (!IsToken("}"))
                        NextToken();

                }

            }
            NextToken();

            return table;
        }

        protected bool IsLiteral
        {
            get
            {
                return Regex.IsMatch(toks[0], "[a-zA-Z]+[0-9a-zA-Z_]*");
            }
        }
        protected bool IsString
        {
            get
            {
                //Match m = Regex.Match(toks[0], "^\"([^\"]*)\"");

                Match m = Regex.Match(toks[0], "^\"([^\"]*)\"");

                if (!m.Success)
                {
                    m = Regex.Match(toks[0], "^\'([^\"]*)\'");
                }
                return m.Success;
            }
        }
        protected bool IsNumber
        {
            get
            {
                //return Regex.IsMatch(toks[0], @"^\d+");

                int myTest;
                return int.TryParse(toks[0], out myTest);
            }
        }
        protected bool IsFloat
        {
            //if (double.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out ValD))
            //CultureInfo ci = CultureInfo.CurrentCulture;
            //var decimalSeparator = ci.NumberFormat.NumberDecimalSeparator;
            //var floatRegex = string.Format(@"[-+]?\d+({0}\d+)?", decimalSeparator);

             get
            {
                //return Regex.IsMatch(toks[0], @"^\d*\.\d+");
                //var regex = new Regex(@"^[0-9]*(?:\.[0-9]*)?$");
                //@"^[-]?[0-9]*(?:\.[0-9]*)?$"
                //return Regex.IsMatch(toks[0], @"^[0-9]*(?:\.[0-9]*)?$");
                return Regex.IsMatch(toks[0], @"^[-]?[0-9]*(?:\.[0-9]*)?$");
            }
        }

        protected bool IsBoolean
        {
            get
            {
                Boolean myBool;
                return Boolean.TryParse(toks[0], out myBool);
            }
        }
        protected bool IsNull
        {
            get
            {
                if (toks[0] == "nil")
                {
                    return true;
                }
                else
                {
                    return false;
                }
            }
        }
        protected string GetToken()
        {
            string v = toks[0];
            //toks.RemoveAt(0);
            NextToken();
            return v;
        }
        protected LuaObject GetString()
        {
            Match m = Regex.Match(toks[0], "^\"([^\"]*)\"");
            if (!m.Success)
            {
                m = Regex.Match(toks[0], "^\'([^\"]*)\'");
            }
            if (!m.Success)
            {
                m = Regex.Match(toks[0], "^([^\"]*)");
            }
            //Match m = Regex.Match(toks[0], "([^\"\\]+)");
            string v = m.Groups[1].Captures[0].Value;
            //toks.RemoveAt(0);
            NextToken();
            return v;
        }
        protected LuaObject GetLiteral()
        {
            //Match m = Regex.Match(toks[0], "[a-zA-Z]+[0-9a-zA-Z_]*");

            //string v = m.Groups[1].Captures[0].Value;

            //NextToken();
            //return v;

            string v = toks[0];
            NextToken();
            return v;
        }
        protected LuaObject GetNumber()
        {
            int v = Convert.ToInt32(toks[0]); //performance
            //int v = Int32.Parse(toks[0]);

            //if (Int32.TryParse(toks[0], out int v))
            //{
            //    v = -1; // we would do something here with a logger perhaps
            //}


            //toks.RemoveAt(0);
            NextToken();
            return v;
        }
        protected LuaObject GetFloat()
        {
            //double v = Convert.ToDouble(toks[0]);
            float v = Single.Parse(toks[0], CultureInfo.InvariantCulture);

            //toks.RemoveAt(0);
            NextToken();
            return v;
        }
        protected LuaObject GetBoolean()
        {
            //bool v = Convert.ToBoolean(toks[0]);
            string v = toks[0];

            //toks.RemoveAt(0);
            NextToken();
            return v;
        }
        protected LuaObject GetNull()
        {
            string v = "";

            if (toks[0] == "nil")
            {
                v = "nil";
            }

            //toks.RemoveAt(0);
            NextToken();
            return v;
        }
        protected void NextToken()
        {
            //if (toks.Count() > 0)
            //{ 
                LastToken = toks[0];
                toks.RemoveAt(0);
            //}
        }
        protected bool IsToken(string s)
        {
            //if (toks.Count() > 0)
            //{
                return toks[0] == s;
            //}
            //else
            //    return false;
        }
    }


    public class LuaObject : System.Collections.IEnumerable
    {
        public object luaobj;

        public LuaObject(object o)
        {
            luaobj = o;
        }

        public System.Collections.IEnumerator GetEnumerator()
        {
            Dictionary<string, LuaObject> dic = luaobj as Dictionary<string, LuaObject>;

            if (dic == null)
            {
                // Retourner un énumérateur vide si dic est null
                return Enumerable.Empty<KeyValuePair<string, LuaObject>>().GetEnumerator();
            }

            return dic.GetEnumerator();
        }

        public LuaObject this[int ix]
        {
            get
            {
                Dictionary<string, LuaObject> dic = luaobj as Dictionary<string, LuaObject>;
                try
                {
                    return dic[ix.ToString()];
                }
                catch (KeyNotFoundException)
                {
                    return null;
                }
            }
        }

        public LuaObject this[string index]
        {
            get
            {
                Dictionary<string, LuaObject> dic = luaobj as Dictionary<string, LuaObject>;
                try
                {
                    return dic[index];
                }
                catch (KeyNotFoundException)
                {
                    return null;
                }
            }
        }

        public static implicit operator string(LuaObject m)
        {
            return m.luaobj as string;
        }

        public static implicit operator int(LuaObject m)
        {
            return (m.luaobj as int? ?? 0);
        }

        public static implicit operator LuaObject(string s)
        {
            return new LuaObject(s);
        }

        public static implicit operator LuaObject(int i)
        {
            return new LuaObject(i);
        }

        public static implicit operator LuaObject(double d)
        {
            return new LuaObject(d);
        }

        public static implicit operator LuaObject(Dictionary<string, LuaObject> dic)
        {
            return new LuaObject(dic);
        }
    }


    //*****************************************************************************************************


    //public class LuaObject : System.Collections.IEnumerable
    //{
    //    public object luaobj;

    //    public IEnumerable<object> Val { get; internal set; }

    //    public LuaObject(object o)
    //    {
    //        luaobj = o;
    //    }
    //    public System.Collections.IEnumerator GetEnumerator()
    //    {
    //        Dictionary<string, LuaObject> dic = luaobj as Dictionary<string, LuaObject>;

    //        //return dic.GetEnumerator();
    //        if (dic == null)
    //        {
    //            // Retourner un énumérateur vide si dic est null
    //            return Enumerable.Empty<KeyValuePair<string, LuaObject>>().GetEnumerator();
    //        }

    //        return dic.GetEnumerator();

    //    }
    //    public LuaObject this[int ix]
    //    {
    //        get
    //        {
    //            Dictionary<string, LuaObject> dic = luaobj as Dictionary<string, LuaObject>;
    //            try
    //            {
    //                return dic[ix.ToString()];
    //            }
    //            catch (KeyNotFoundException)
    //            {
    //                return null;
    //            }
    //        }
    //    }
    //    public LuaObject this[string index]
    //    {
    //        get
    //        {
    //            Dictionary<string, LuaObject> dic = luaobj as Dictionary<string, LuaObject>;
    //            try
    //            {
    //                return dic[index];
    //            }
    //            catch (KeyNotFoundException)
    //            {
    //                return null;
    //            }
    //        }
    //    }
    //    public static implicit operator string(LuaObject m)
    //    {
    //        return m.luaobj as string;
    //    }
    //    public static implicit operator int(LuaObject m)
    //    {
    //        return (m.luaobj as int? ?? 0);
    //    }
    //    public static implicit operator LuaObject(string s)
    //    {
    //        return new LuaObject(s);
    //    }
    //    public static implicit operator LuaObject(int i)
    //    {
    //        return new LuaObject(i);
    //    }
    //    public static implicit operator LuaObject(double d)
    //    {
    //        return new LuaObject(d);
    //    }
    //    public static implicit operator LuaObject(Dictionary<string, LuaObject> dic)
    //    {
    //        return new LuaObject(dic);
    //    }
    //}
}
