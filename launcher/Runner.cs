using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using VirtualLib;
using PNUnit.Launcher;

using System.Runtime.Remoting;
using System.Runtime.Remoting.Lifetime;

using log4net;

using NUnit.Core;

using PNUnit.Framework;

namespace PNUnit.Launcher
{
	public class Runner: MarshalByRefObject, IPNUnitServices
	{

		private static readonly ILog log = LogManager.GetLogger(typeof(Runner));
        private static int paramCount = 7;

		private const string agentkey = "_AGENT";        

		private ParallelTest mTestGroup;
		private Thread mThread = null;
		private IList mResults = null;
		private Object mResultLock = new Object();
		private ManualResetEvent mFinish;
		private Hashtable mBarriers;
		private int mLaunchedTests;
		private Hashtable mBarriersOfTests;

//        private string[] acceptedParams = new string[] { "OS", "DB", "DBLOC", "AUTH", "BITNESS" };
        private string[] acceptedParams = new string[] { "OS", "DB", "DBLOC", "AUTH", "BITNESS", "EPOADMIN","EPOPASSWORD" };

		public Runner(ParallelTest test)
		{
			mTestGroup = test;
			mResults = new ArrayList();
		}

		public string TestGroupName
		{
			get{ return mTestGroup.Name; }
		}

		public void Run()
		{
			if( mTestGroup.Tests.Length == 0 )
			{
				log.Fatal("No tests to run, exiting");
				return;
			}
			mThread = new Thread(new ThreadStart(ThreadProc));
			mThread.Start();
		}

		public void Join()
		{
			if( mThread != null )
				mThread.Join();
		}

		private void ThreadProc()
		{
			log.DebugFormat("Thread created for TestGroup {0} with {1} tests", mTestGroup.Name, mTestGroup.Tests.Length);
			mFinish = new ManualResetEvent(false);
			mBarriers = new Hashtable();
			mBarriersOfTests = new Hashtable();
            List<VM> vmList = new List<VM>();            

			RemotingServices.Marshal(this, mTestGroup.Name);

			mLaunchedTests = 0;
			foreach( TestConf test in mTestGroup.Tests )
			{
                bool parseStatus = true;
                int count = 0;
                VM testVM = null;
                string templateName;
                string systemName;
                string[] dnsList;
                string workGroupPassword;
                string domainAdmin;
                string domainPassword;
                string joinDomain;
                string productId;
                bool deployStatus;

				if (test.Machine.StartsWith(agentkey))
					test.Machine = mTestGroup.Agents[int.Parse(test.Machine.Substring(agentkey.Length))-1];               
                
                //Get Parameters for creating VM                
                IDictionary<string, string> vmParams = new Dictionary<string, string>();
                
                //parse parameters and check if they are the correct parameters
                foreach (string s in test.TestParams)
                {                    
                    string[] keyValue = s.Split(new Char[] { '=' });
                    if (keyValue.Length != 2 || !(((IList)acceptedParams).Contains(keyValue[0]))) //if not key=value format or key is not in approved list
                    {                                                
                        parseStatus = false;
                        break;
                    }
                    else
                    {
                        log.DebugFormat("Parameter parse successful for test ", test.Name); 
                        vmParams.Add(keyValue[0], keyValue[1]);
                    }                    
                    count = count + 1;
                }

                if (parseStatus == false)
                {                    
                    log.ErrorFormat("Incorrect parameter in Group {0} Test {1}, skipping test", mTestGroup.Name, test.Name, count + 1);
                    continue;
                }
                if (count != paramCount)
                {
                    Console.WriteLine("Group {0} Test {1} Not all parameters required for deploying VM specified, skipping test", mTestGroup.Name, test.Name);
                    log.ErrorFormat("Group {0} Test {1} Not all parameters required for deploying VM specified, skipping test", mTestGroup.Name, test.Name);
                    continue;
                }

                if (vmParams["OS"].StartsWith("WIN2003"))
                {
                    testVM = new Win2003VM(Launcher.automationHost);
                }
                else if (vmParams["OS"].StartsWith("WIN2008R2"))
                {
                    testVM = new Win2008R2VM(Launcher.automationHost);
                }
                else if (vmParams["OS"].StartsWith("WIN2008"))
                {
                    testVM = new Win2008VM(Launcher.automationHost);
                }
                else
                {
                    Console.WriteLine("Invalid OS type specified, exiting");
                    Environment.Exit(1);
                }

                //log.InfoFormat("Creating VM for test {0}" + test.Name);

                templateName = "TM_" + vmParams["OS"] + vmParams["BITNESS"];
                systemName = "W" + vmParams["OS"].Substring(3, 6) + vmParams["BITNESS"] + new Random().Next(10000, 99999).ToString();
                //systemName = new Random().Next(1000000, 9999999).ToString();
                dnsList = new string[] { Launcher.environment.IniReadValue("ENVIRONMENTINFO","DNS" + vmParams["AUTH"]) };
                workGroupPassword = Launcher.environment.IniReadValue("ENVIRONMENTINFO","WORKGROUPPASSWORD");
                domainAdmin = Launcher.environment.IniReadValue("ENVIRONMENTINFO","ADMIN" + vmParams["AUTH"]);
                domainPassword = Launcher.environment.IniReadValue("ENVIRONMENTINFO","PASSWORD" + vmParams["AUTH"]);
                joinDomain = Launcher.environment.IniReadValue("ENVIRONMENTINFO","DOMAIN" + vmParams["AUTH"]);
                productId = Launcher.environment.IniReadValue("PRODUCTID", vmParams["OS"] + vmParams["BITNESS"]);

                testVM.defineSysprepParameters(templateName, systemName, dnsList, workGroupPassword, domainAdmin, domainPassword, joinDomain, productId);
                
                //Parameters set, deploy VM
                log.InfoFormat("Set to deploy VM from template {0}", templateName);
                log.DebugFormat("Template parameters: systemName:{0} primaryDns:{1} workGroupPassword:{2} domainAdmin:{3} domainPassword:{4} domain:{5} productId:{6}", systemName, dnsList[0], workGroupPassword, domainAdmin, domainPassword, joinDomain, productId);
                deployStatus = testVM.deploy();               
                //debug line                
                //deployStatus = true;
                //testVM.setName("W2003STX8688225");                
                //testVM.setName("W2008DCX8637933");


                //set test to vm
                testVM.setTestName(test.Name);
                
                string ePOBuildPath = Launcher.environment.IniReadValue("BUILD","PATH");
                log.DebugFormat("ePO Build at {0}", ePOBuildPath);

                if(deployStatus == true)
                {
                    log.InfoFormat("Deploy of template {0} to system {1} succeeded", templateName, systemName);
                    bool copyStatus;
                    //debug line
                    //copyStatus = true;
                
                    vmList.Add(testVM);
                    /* Wait 120 seconds for the deployment process to complete
                     * During this time the system restarts 2 times,
                     * since the system is being configured during this time waitForLogon() will be inconsistent
                     */
                    log.InfoFormat("Waiting atleast 2 minutes for VM customization to complete for {0}", systemName);
                    //System.Threading.Thread.Sleep(120000);

                    testVM.waitForLogon(100);

                    System.Threading.Thread.Sleep(5000); //wait 5 seconds for stabilization
                    log.InfoFormat("Copying build, agent and test files to VM {0}",systemName);
                    copyStatus = testVM.copyRequiredFilesToVM(Launcher.environment.IniReadValue("BUILD", "EPOPATH"), Launcher.environment.IniReadValue("AGENT", "ZIP"), Launcher.environment.IniReadValue("AGENT", "TESTS"), @"f:\autoinstallproject\uzext.exe", @"f:\autoinstallproject\uzcomp.exe");                    
                    
                    if (copyStatus)
                    {
                        log.InfoFormat("Copy successful to VM {0}",systemName);
                        log.InfoFormat("Staging VM {0}", systemName);
                        testVM.stage();
                        log.InfoFormat("Waiting for user logon in VM {0}", systemName);
                        testVM.waitForLogon(20);
                        log.InfoFormat("User Logged on in VM {0}", systemName);
                    }
                    else
                    {
                        log.ErrorFormat("File copy failed to VM {0}, skipping test", systemName);
                        continue;
                    }
                }
                else
                {
                    log.ErrorFormat("Deploy of template {0} to system {1} failed", templateName, systemName);
                    continue;
                }
                /*
                adminUserName – EPOADMIN
                adminPassword – EPOPASSWORD
                databaseServername – DBSERVER
                databseUserName – DBUSERNAME
                databaseDomain – DBDOMAIN
                databasePassword – DBPASSWORD
                databaseAuth – DBAUTH
                */
                test.Machine = testVM.getName() +":" + Launcher.environment.IniReadValue("AGENT", "PORT"); ;

                //Build epo parameters
                // Fill in SQL Server name, instance name, db username, db password, db domain, db auth

                /*SQL Server name - DBSERVER
                 *  If SQL is remote then use ntlmvalue
                */

                if (vmParams["DBLOC"].Equals("REMOTE"))
                {
                    vmParams.Add("DBSERVER", Launcher.environment.IniReadValue("ENVIRONMENTINFO", "REMOTE" + vmParams["DB"] + vmParams["AUTH"]));
                }
                else
                {
                    vmParams.Add("DBSERVER", testVM.getName());
                }

                if (vmParams["DBLOC"].Equals("REMOTE"))
                {
                    vmParams.Add("DBINSTANCE", Launcher.environment.IniReadValue("ENVIRONMENTINFO", vmParams["DBLOC"] + vmParams["DB"] + vmParams["AUTH"] + "INSTANCE"));
                }
                else
                {
                    vmParams.Add("DBINSTANCE", Launcher.environment.IniReadValue("ENVIRONMENTINFO", vmParams["DBLOC"] + vmParams["DB"] + "INSTANCE"));
                }
                
                /*Database user name - DBUSERNAME
                 * If AUTH is NTLM use NT else use sa
                */
                if (vmParams["AUTH"].StartsWith("NT"))
                {
                    vmParams.Add("DBUSERNAME", Launcher.environment.IniReadValue("ENVIRONMENTINFO", "ADMIN" + vmParams["AUTH"]));
                }
                else
                {
                    vmParams.Add("DBUSERNAME", "sa");
                }

                /*Database password
                 * If auth is NTLM, use domain password
                 * else find sa password for domain
                

                /*Database Domain*/
                if(vmParams["AUTH"].StartsWith("NT"))
                {
                    vmParams.Add("DBDOMAIN", Launcher.environment.IniReadValue("ENVIRONMENTINFO", "DOMAIN" + vmParams["AUTH"]));
                    vmParams.Add("DBPASSWORD", Launcher.environment.IniReadValue("ENVIRONMENTINFO", "PASSWORD" + vmParams["AUTH"]));
                    vmParams.Add("DBAUTH", "1");
                }
                else
                {
                    vmParams.Add("DBDOMAIN", "");
                    vmParams.Add("DBAUTH", "2");
                    vmParams.Add("DBPASSWORD", Launcher.environment.IniReadValue("ENVIRONMENTINFO", vmParams["DBLOC"] + vmParams["DB"] + vmParams["AUTH"] + "PASS"));
                }

                //Add epo Path
                vmParams.Add("EPOPATH",Launcher.environment.IniReadValue("BUILD","AGENTPATH"));

                string[] newTestParams = new string[14];                
                
                int i = 0;
                foreach(string s in vmParams.Keys)
                {
                    newTestParams[i] = s + "=" + vmParams[s];
                    ++i;
                }

                log.DebugFormat("Test Parameters for test {0} on {1} - {2}", test.Name, test.Machine, String.Join(",", newTestParams));

                test.TestParams = newTestParams;

				log.InfoFormat("Starting {0} test {1} on {2}", mTestGroup.Name, test.Name, test.Machine);



				// contact the machine
				try
				{
					IPNUnitAgent agent = (IPNUnitAgent)
						Activator.GetObject(
						typeof(IPNUnitAgent), 
						string.Format(
						"tcp://{0}/{1}", 
						test.Machine, 
						PNUnit.Framework.Names.PNUnitAgentServiceName));

					lock( mResultLock )
					{
						++mLaunchedTests;
					}
                                    
					agent.RunTest(new TestInfo(test.Name, test.Assembly, test.TestToRun, test.TestParams, this));
				}
				catch( Exception e )
				{
					log.ErrorFormat(
						"An error occurred trying to contact {0} [{1}]", 
						test.Machine, e.Message);

					lock( mResultLock )
					{
						--mLaunchedTests;
					}
				}
			}
            
			log.DebugFormat("Thread going to wait for results for TestGroup {0}", mTestGroup.Name);
            mFinish.Reset();
			while( HasToWait() )
				// wait for all tests to end
				mFinish.WaitOne();

			log.DebugFormat("Thread going to wait for NotifyResult to finish for TestGroup {0}", mTestGroup.Name);
			Thread.Sleep(500); // wait for the NotifyResult call to finish
			RemotingServices.Disconnect(this);
			log.DebugFormat("Thread going to finish for TestGroup {0}", mTestGroup.Name);
            
            // For each VM copy logs and delete            
            foreach (VM vm in vmList)
            {
                vm.copyLogsToHost(@"c:\" + vm.getTestName(), Launcher.runLogPath);
                vm.delete();
            }
            
		}
        
		private bool HasToWait()
		{
			lock( mResultLock )
			{
				return (mLaunchedTests > 0) && (mResults.Count < mLaunchedTests);
			}
		}

		public PNUnitTestResult[] GetTestResults()
		{
			lock(mResultLock)
			{
				PNUnitTestResult[] result = new PNUnitTestResult[mResults.Count];
				int i = 0;
				foreach( PNUnitTestResult res in mResults )
					result[i++] = res;
                
				return result;
			}
		}

		#region MarshallByRefObject
		// Lives forever
		public override object InitializeLifetimeService()
		{
			return null;
		}
		#endregion

		#region IPNUnitServices

		public void NotifyResult(string TestName, PNUnitTestResult result)
		{   
			log.DebugFormat("NotifyResult called for TestGroup {0}, Test {1}",
				mTestGroup.Name, TestName);
			lock(mResultLock)
			{
				log.DebugFormat("NotifyResult lock entered for TestGroup {0}, Test {1}",
					mTestGroup.Name, TestName);
                
				mResults.Add(result);
				if( mResults.Count == mLaunchedTests )
				{
					log.DebugFormat("All the tests notified the results, waking up. mResults.Count == {0}",
						mResults.Count);
					mFinish.Set();
				}

			}   
			lock( mBarriers )
			{
				if( mBarriersOfTests.Contains(TestName) )
				{
					log.DebugFormat("Going to abandon barriers of test {0}", TestName);
					IList list = (IList) mBarriersOfTests[TestName];
					foreach( string barrier in list )
					{
						log.DebugFormat("Abandoning barrier {0}", barrier);
						((Barrier)mBarriers[barrier]).Abandon();
					}
				}
			}
			log.DebugFormat("NotifyResult finishing for TestGroup {0}, Test {1}.",
				mTestGroup.Name, TestName); 
			log.InfoFormat("Result for TestGroup {0}, Test {1}: {2}",
				mTestGroup.Name, TestName, result.IsSuccess ? "PASS" : "FAIL");
		}

		public void InitBarrier(string TestName, string barrier, int Max)
		{
			lock( mBarriers )
			{
				if( ! mBarriers.Contains(barrier) )
				{
					mBarriers.Add(barrier, new Barrier(Max));
				}

				if( mBarriersOfTests.Contains(TestName) )
				{
					IList listofbarriers = (IList) mBarriersOfTests[TestName];
					listofbarriers.Add(barrier);
					log.DebugFormat("Adding barrier {0} to {1}", barrier, TestName);
				}
				else
				{
					ArrayList list = new ArrayList();
					list.Add(barrier);
					log.DebugFormat("Adding barrier {0} to {1}", barrier, TestName);
					mBarriersOfTests.Add(TestName, list);
				}

                
			}
		}

		public void InitBarrier(string TestName, string barrier)
		{
			InitBarrier(TestName, barrier, mTestGroup.Tests.Length);
		}

		private const int indexStartBarrier = 2;
		private const int indexEndBarrier = 3;

		public void InitBarriers (string TestName)
		{
			Hashtable barriers = new Hashtable();
			for (int i=1; i< mTestGroup.Tests.Length; i++)
			{
				string sb = mTestGroup.Tests[i].TestParams[indexStartBarrier];
				string eb = mTestGroup.Tests[i].TestParams[indexEndBarrier];

				if (sb.Trim() != "") 
				{
					if(barriers.Contains(sb))
						barriers[sb] = (int)barriers[sb]+1;
					else
						barriers[sb] = 1;
				}

				if (eb.Trim() != "") 
				{
					if(barriers.Contains(eb))
						barriers[eb] = (int)barriers[eb]+1;
					else
						barriers[eb] = 1;
				}

			}

			foreach (string key in barriers.Keys)
			{
				if (!key.Equals(Names.ServerBarrier) && !key.Equals(Names.EndBarrier))
					InitBarrier (TestName, key, (int)barriers[key]);
                
				InitBarrier (TestName, Names.ServerBarrier);
				InitBarrier (TestName, Names.EndBarrier);
			}
		}

		public void EnterBarrier(string barrier)
		{
			log.DebugFormat("Entering Barrier {0}", barrier);
			((Barrier)mBarriers[barrier]).Enter();
		}

		#endregion
	}

	/*    public class TestResult
		{
			public string TestName;
			public string Result;

			public TestResult(string name, string result)
			{
				TestName = name;
				Result = result;
			}
		}*/
}
