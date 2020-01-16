using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using System.Net.Mail;
using System.ServiceProcess;
using System.Text;
using System.Threading.Tasks;

namespace DailyPL
{
    class Program
    {
        static void Main(string[] args)
        {
            //ServiceController controller = new ServiceController("PropB6Service");
            //if (controller.Status == ServiceControllerStatus.Running)
            //{
            //    controller.Stop();

            //}
            //controller.WaitForStatus(ServiceControllerStatus.Stopped);

            string conStr = "user id = sa;password=JST@123;server=EC2AMAZ-V1NLV3G;database=OrderBookDB;connection timeout=15;MultipleActiveResultSets=true";
            SqlDataReader myReader = null;

            using (SqlConnection myConnection = new SqlConnection(conStr))
            {
                myConnection.Open();
                string getStrategiesSQL = "select StratID, FirstCurrencyID, SecondCurrencyID, CurrentPosition " +
                    "from OrderBookDB.dbo.PropStrategies " +
                    "where IsEnabled = 1 order by StratID";
                SqlCommand myCommand = new SqlCommand(getStrategiesSQL, myConnection);
                myReader = myCommand.ExecuteReader();

                if (myReader.HasRows)
                {
                    while (myReader.Read())
                    {
                        int stratID = myReader.GetInt32(0);
                        int firstCurrencyID = myReader.GetInt32(1);
                        int secondCurrencyID = myReader.GetInt32(2);
                        double currentPos = (double)myReader.GetDecimal(3);
                        double totalBuys;
                        double totalSales;
                        double stratPL;


                        // get last price 
                        //string getLastPriceSQL = "select top 1 ask, bid from TickerData.dbo.DailyNBBO where " +
                        //     "Currency1 = " + firstCurrencyID.ToString() + " and Currency2 = " + secondCurrencyID.ToString() +
                        //     " and Type = 'CLOSE' order by Date desc";

                        string getLastPriceSQL = "select top 1 ask, bid from TickerData.dbo.Ticker " +
                                "where ExchangeCurrencyId in " +
                                "(select id from TickerData.dbo.ExchangeCurrency " +
                                "where FirstCurrencyid = " + firstCurrencyID.ToString() + " and " +
                                "SecondCurrencyId = " + secondCurrencyID.ToString() + ") order by createddate desc";

                        SqlDataReader lastPriceReader = null;
                        SqlCommand lastPriceCommand = new SqlCommand(getLastPriceSQL, myConnection);
                        lastPriceReader = lastPriceCommand.ExecuteReader();
                        lastPriceReader.Read();
                        double lastPrice = (double)(lastPriceReader.GetDecimal(0) + lastPriceReader.GetDecimal(1)) / 2;
                        lastPriceReader.Close();
                        double sizingPrice = 1.0;
                        lastPrice = Math.Round(lastPrice, 8);

                        // if second currency not USD
                        if (secondCurrencyID != 4)
                        {
                            getLastPriceSQL = "select top 1 ask, bid from TickerData.dbo.Ticker " +
                                "where ExchangeCurrencyId in " +
                                "(select id from TickerData.dbo.ExchangeCurrency " +
                                "where FirstCurrencyid = " + secondCurrencyID.ToString() + " and " +
                                "SecondCurrencyId = 4) order by createddate desc";
                            lastPriceReader = null;
                            lastPriceCommand = new SqlCommand(getLastPriceSQL, myConnection);
                            lastPriceReader = lastPriceCommand.ExecuteReader();
                            lastPriceReader.Read();
                            sizingPrice = (double)(lastPriceReader.GetDecimal(0) + lastPriceReader.GetDecimal(1)) / 2;
                            lastPriceReader.Close();
                        }

                        // get buy PL
                        getLastPriceSQL = "select sum(TradeQty) as TotBuys, " +
                            "round(sum(TradeQty * (" + lastPrice.ToString("F8") + " - TradePrice) - Fees ),8) as TotBuyPL " +
                            "from OrderBookDB.dbo.PropTradeBlotter where TradeQty > 0 and StratID = " + stratID.ToString();
                        lastPriceReader = null;
                        lastPriceCommand = new SqlCommand(getLastPriceSQL, myConnection);
                        lastPriceReader = lastPriceCommand.ExecuteReader();
                        //if (lastPriceReader.HasRows)
                        lastPriceReader.Read();
                        if (!lastPriceReader.IsDBNull(0))
                        {

                            totalBuys = (double)lastPriceReader.GetDecimal(0);
                            decimal tempPL = lastPriceReader.GetDecimal(1);
                            stratPL = (double)tempPL;
                            lastPriceReader.Close();
                        }
                        else
                        {
                            totalBuys = 0;
                            stratPL = 0;
                        }
                        // get sell PL
                        getLastPriceSQL = "select sum(TradeQty) as TotSales, " +
                            "sum(TradeQty * (" + lastPrice.ToString("F8") + " - TradePrice) - Fees) as TotSellPL " +
                            "from OrderBookDB.dbo.PropTradeBlotter where TradeQty < 0 and StratID = " + stratID.ToString();
                        lastPriceReader = null;
                        lastPriceCommand = new SqlCommand(getLastPriceSQL, myConnection);
                        lastPriceReader = lastPriceCommand.ExecuteReader();
                        lastPriceReader.Read();
                        if (!lastPriceReader.IsDBNull(0))
                        {

                            totalSales = (double)lastPriceReader.GetDecimal(0);
                            stratPL += (double)lastPriceReader.GetDecimal(1);
                            lastPriceReader.Close();
                        }
                        else
                        {
                            totalSales = 0;
                        }

                        stratPL *= sizingPrice;

                        // insert into DB
                        double totalPos = Math.Round(totalBuys + totalSales, 1);
                        if (Math.Round(currentPos, 1) == totalPos)
                        {
                            string newOrderSQL2 = "insert into OrderBookDB.dbo.PropMtoM " +
                                "(MarkDate, StratID, EODPosition, EODPrice, TotalPL) " +
                                "values (getdate()," + stratID.ToString() + "," + currentPos.ToString() +
                                "," + lastPrice.ToString() + "," + stratPL.ToString() + ")";
                            using (SqlCommand newTradeComm = new SqlCommand(newOrderSQL2))
                            {
                                newTradeComm.Connection = myConnection;
                                newTradeComm.CommandType = System.Data.CommandType.Text;
                                newTradeComm.CommandText = newOrderSQL2;
                                //newTradeComm.ExecuteNonQuery();
                            }

                        }
                        else
                        {
                            string newOrderSQL2 = "insert into OrderBookDB.dbo.PropMtoM " +
                                                            "(MarkDate, StratID, EODPosition, EODPrice, TotalPL) " +
                                                            "values (getdate()," + stratID.ToString() + "," + (totalBuys+totalSales).ToString() +
                                                            "," + lastPrice.ToString() + "," + stratPL.ToString() + ")";
                            using (SqlCommand newTradeComm = new SqlCommand(newOrderSQL2))
                            {
                                newTradeComm.Connection = myConnection;
                                newTradeComm.CommandType = System.Data.CommandType.Text;
                                newTradeComm.CommandText = newOrderSQL2;
                                //newTradeComm.ExecuteNonQuery();
                            }
                        }
                    }
                }
                // **************************** GET TOTAL SUBSCRIPTIONS **************************

                //double initSubscriptions = 10000.0;
                //double newSubscriptions = 0;

                string getSubsSQL = "select sum(transqty) as TotSubs from OrderBookDB.dbo.PropBalances " +
                                "where StratID = 777";
                SqlDataReader subsReader = null;
                SqlCommand subsCommand = new SqlCommand(getSubsSQL, myConnection);
                subsReader = subsCommand.ExecuteReader();
                subsReader.Read();
                double initSubscriptions = (double)subsReader.GetDecimal(0);
                subsReader.Close();

                // ***********************************************************************************

                string eMailMessage = "Daily Report<br><br>Current Positions<br>";
                // get balances
                string getBalancesSQL = "select a.CurrencyID, sum(a.TransQty) as Total, b.Code " +
                    "from OrderBookDB.dbo.PropBalances a inner join TickerData.dbo.Currency b " +
                    "on a.CurrencyID = b.Id where a.CurrencyID <> 4 and a.StratID <> 666 group by a.CurrencyID, b.Code order by b.Code";
                myCommand = new SqlCommand(getBalancesSQL, myConnection);
                myReader = myCommand.ExecuteReader();
                double currencyPrice;
                double totalNAV = 0;
                while (myReader.Read())
                {
                    int currencyID = myReader.GetInt32(0);
                    double totalQty = (double)myReader.GetDecimal(1);
                    string currCode = myReader.GetString(2);

                    // get last price
                    string getLastPriceSQL = "select top 1 ask, bid from TickerData.dbo.Ticker " +
                                "where ExchangeCurrencyId in " +
                                "(select id from TickerData.dbo.ExchangeCurrency " +
                                "where FirstCurrencyid = " + currencyID.ToString() + " and " +
                                "SecondCurrencyId = 4) order by createddate desc"; 
                    SqlDataReader lastPriceReader = null;
                    SqlCommand lastPriceCommand = new SqlCommand(getLastPriceSQL, myConnection);
                    lastPriceReader = lastPriceCommand.ExecuteReader();
                    lastPriceReader.Read();
                    currencyPrice = (double)(lastPriceReader.GetDecimal(0) + lastPriceReader.GetDecimal(1)) / 2;
                    lastPriceReader.Close();


                    eMailMessage += currCode.ToString() + " : " + Math.Round(totalQty, 2).ToString() +
                        " LastPrice: " + Math.Round(currencyPrice, 6).ToString() + "<br>";

                    totalNAV += currencyPrice * totalQty;


                }
                // get USD balance
                string getUSDBalance = "select sum(TransQty) as Total " +
                    "from OrderBookDB.dbo.PropBalances where CurrencyID = 4 and StratID <> 666";
                myCommand = new SqlCommand(getUSDBalance, myConnection);
                myReader = myCommand.ExecuteReader();

                while (myReader.Read())
                {
                    double USDQty = (double)myReader.GetDecimal(0);
                    totalNAV += USDQty;
                    eMailMessage += "<br>USD : " + Math.Round(USDQty - initSubscriptions, 2).ToString() + "<br><br>";

                }
                // get yesterday NAV
                string getYestNAV = "select top 1 (TotalNAV - subscriptions) as YPL, Celsius " +
                    "from OrderBookDB.dbo.PropPLRecords order by RecordDate desc, id desc";
                myCommand = new SqlCommand(getYestNAV, myConnection);
                myReader = myCommand.ExecuteReader();
                double newCelsius = 0;

                while (myReader.Read())
                {
                    double yestNAV = (double)myReader.GetDecimal(0);
                    double yestCelsius = (double)myReader.GetDecimal(1);
                    double dailyPL = totalNAV - yestNAV - initSubscriptions;
                    eMailMessage += "DailyPL : $" + Math.Round(dailyPL, 2).ToString() + "  ("+Math.Round(100*dailyPL/yestCelsius,4).ToString()+"%)<br>";
                    newCelsius = yestCelsius+dailyPL;
                }

                // get last Friday NAV
                string weekNAV = "select top 1 (TotalNAV - Subscriptions) as YPL, Celsius from orderbookdb.dbo.propplrecords " +
                    "where datepart(DW, recorddate) = 6 order by recorddate desc";
                myCommand = new SqlCommand(weekNAV, myConnection);
                myReader = myCommand.ExecuteReader();

                while (myReader.Read())
                {
                    double yestNAV = (double)myReader.GetDecimal(0);
                    double yestCelsius = (double)myReader.GetDecimal(1);
                    double weeklyPL = totalNAV - yestNAV - initSubscriptions;
                    eMailMessage += "WeeklyPL : $" + Math.Round(weeklyPL, 2).ToString() + " ("+Math.Round(100*weeklyPL/yestCelsius,4).ToString()+"%)<br>";

                }

                // get Monthly NAV 
                string monthNAV = "select top 1 (TotalNAV - Subscriptions) as YPL, Celsius from orderbookdb.dbo.propplrecords " +
                    "where month(recorddate) != month(getdate()) order by recorddate desc ";
                myCommand = new SqlCommand(monthNAV, myConnection);
                myReader = myCommand.ExecuteReader();

                if (myReader.HasRows)
                {
                    while (myReader.Read())
                    {
                        double yestNAV = (double)myReader.GetDecimal(0);
                        double yestCelsius = (double)myReader.GetDecimal(1);
                        double weeklyPL = totalNAV - yestNAV - initSubscriptions;
                        eMailMessage += "MonthlyPL : $" + Math.Round(weeklyPL, 2).ToString() + " (" + Math.Round(100 * weeklyPL / yestCelsius, 4).ToString() + "%)<br>";
                    }
                }
                else
                {

                    double yestNAV = 0;
                    eMailMessage += "MonthlyPL : $" + Math.Round(totalNAV - yestNAV - initSubscriptions, 2).ToString() + "<br>";


                }

                // get yearly NAV
                string getYENAV = "select top 1 (TotalNAV - subscriptions) as YPL, Celsius " +
                    "from OrderBookDB.dbo.PropPLRecords where Year(RecordDate) < Year(getdate()) order by recorddate desc";
                myCommand = new SqlCommand(getYENAV, myConnection);
                myReader = myCommand.ExecuteReader();
                

                while (myReader.Read())
                {
                    double yestNAV = (double)myReader.GetDecimal(0);
                    double yestCelsius = (double)myReader.GetDecimal(1);
                    double dailyPL = totalNAV - yestNAV - initSubscriptions;
                    eMailMessage += "YTD PL : $" + Math.Round(dailyPL, 2).ToString() + "  (" + Math.Round(100 * dailyPL / yestCelsius, 4).ToString() + "%)<br>";
                    
                }

                // insert
                string newOrderSQL = "insert into OrderBookDB.dbo.PropPLRecords " +
                            "(RecordDate, TotalNAV,Subscriptions,Celsius) " +
                            "values (getdate()," + totalNAV.ToString() + "," +
                            initSubscriptions.ToString() + "," + newCelsius.ToString() + ")";
                using (SqlCommand newTradeComm = new SqlCommand(newOrderSQL))
                {
                    newTradeComm.Connection = myConnection;
                    newTradeComm.CommandType = System.Data.CommandType.Text;
                    newTradeComm.CommandText = newOrderSQL;
                    //newTradeComm.ExecuteNonQuery();
                }



                // send email
                SendEmail("pangsupun@jstsystems.com", "Trading Daily Report TEST", eMailMessage);
                //SendEmail("sfreeman@jstsystems.com", "Trading Daily Report", eMailMessage);
                //SendEmail("tmorakis@jstsystems.com", "Trading Daily Report", eMailMessage);
                //SendEmail("amortberg@jstsystems.com", "Trading Daily Report", eMailMessage);
                
            }
            //if (controller.Status == ServiceControllerStatus.Stopped)
            //{
            //    controller.Start();
            //}




        }

        public static void SendEmail(string to, string subject, string body)
        {
            const String FROM = "pangsupun@jstsystems.com";
            const String FROMNAME = "Pallop Angsupun";
            String TO = to;
            const String SMTP_USERNAME = "AKIAI5DNN2LGU6MSQFZA";
            const String SMTP_PASSWORD = "AuVU9EHHYC5PgHJk2U54kET+VpqxEwBwpwnVCRocNhk4";
            //const String CONFIGSET = "ConfigSet";
            const String HOST = "email-smtp.us-east-1.amazonaws.com";
            const int PORT = 587;


            // Create and build a new MailMessage object    
            MailMessage message = new MailMessage();
            message.IsBodyHtml = true;

            message.From = new MailAddress(FROM, FROMNAME);
            string[] emails = to.Split(new char[] { ',' });

            foreach (string s in emails)
            {
                if (s.Contains("bcc:"))
                {
                    string bccAddress = s.Split(new string[] { "bcc:" }, StringSplitOptions.RemoveEmptyEntries)[1];
                    message.Bcc.Add(new MailAddress(bccAddress));
                }
                else
                {
                    message.To.Add(new MailAddress(s));
                }
            }
            message.Subject = subject;
            message.Body = body;

            SmtpClient client =
                new SmtpClient(HOST, PORT);
            client.Credentials =
                new System.Net.NetworkCredential(SMTP_USERNAME, SMTP_PASSWORD);
            client.EnableSsl = true;

            try
            {
                Console.WriteLine("Sending Alert Email..");
                client.Send(message);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error message: " + ex.Message);
            }
        }
    

    }
}
