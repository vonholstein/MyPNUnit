using System;
using System.Diagnostics;
using System.IO;

using NUnit.Framework;
using PNUnit.Framework;

namespace Codice.Test
{
	public class CmdRunner
	{
		private static bool IsWindows()
		{
			switch (Environment.OSVersion.Platform)
			{
				case PlatformID.Win32Windows: 
				case PlatformID.Win32NT:
					return true;
				default:
					return false;
			}
		}

		private static string EscapeArgs(string args)
		{
			if( IsWindows() )
				return args;
			else
				return args.Replace("#", "\\#");
		}

		private static Process InternalRun(string cmd, string workingdir)
		{
			Process p = new Process();
			string[] args = cmd.Split(' ');            
			p.StartInfo.FileName = args[0];
			p.StartInfo.WorkingDirectory = workingdir;
			p.StartInfo.Arguments = EscapeArgs(cmd.Substring(args[0].Length));
			p.StartInfo.CreateNoWindow = false;
			p.StartInfo.RedirectStandardOutput = true;
			p.StartInfo.RedirectStandardInput = true;
			p.StartInfo.RedirectStandardError = true;
			p.StartInfo.UseShellExecute = false;
			p.Start();                
			return p;
		}

		private static Process cmdProc = null;
		private static string COMMAND_RESULT = "CommandResult";
		public static int RunAndWait(string cmd, string workingdir, out string output, out string error)
		{

			if( cmd.StartsWith("cm") )
			{
				workingdir = Path.GetFullPath(workingdir);
				if( cmdProc == null )
				{
					cmdProc = InternalRun("cm shell", workingdir);
				}

				string command = cmd.Substring(3);
				cmdProc.StandardInput.WriteLine("{0} -path={1}", command, workingdir);
				bool bDone = false;
				output = "";
				string line;
				int result = 0;
				while( !bDone )
				{
					line = cmdProc.StandardOutput.ReadLine();
					if( line.StartsWith(COMMAND_RESULT) )
					{
						bDone = true;
						result = Convert.ToInt32(line.Substring(COMMAND_RESULT.Length + 1));
					}
					else
						output += line + "\n";
				}
				error = "";
				return result;
			}
			else
			{
				Process p = InternalRun(cmd, workingdir);            
				output = p.StandardOutput.ReadToEnd();
				error = p.StandardError.ReadToEnd();
				p.WaitForExit();

				return p.ExitCode;
			}
		}


		public static void TerminateShell ()
		{
			if (cmdProc != null)
			{
				cmdProc.StandardInput.WriteLine("exit");
			}
		}
		public static Process Run(string cmd, string workingdir)
		{
			Process p = new Process();
			string[] args = cmd.Split(' ');
			p.StartInfo.FileName = args[0];
			p.StartInfo.WorkingDirectory = workingdir;
			p.StartInfo.Arguments = cmd.Substring(args[0].Length);            
			p.StartInfo.CreateNoWindow = false;
			p.StartInfo.UseShellExecute = false;
			p.StartInfo.RedirectStandardInput = true;
			p.StartInfo.RedirectStandardOutput = true;
			p.Start();                
			return p;
		}

		public static void ExecuteCommand(string command, string path)
		{            
			string output, error;
			Assert.IsTrue( InternalExecuteCommand(command, path, out output, out error) == 0, output);                    
		}

		public static int ExecuteCommandWithResult(string command, string path)
		{
			string output, error;
			return InternalExecuteCommand(command, path, out output, out error);
		}

		public static string ExecuteCommandWithStringResult(string command, string path)
		{
			string output, error;
			InternalExecuteCommand(command, path, out output, out error);
			return output;
		}

		private static int InternalExecuteCommand(string command, string path, out string output, out string error)
		{
			output = "";
			error = "";

			PNUnitServices.Get().WriteLine(string.Format("{0}$ {1}", path, command));
			try            
			{
				int result;

				result = CmdRunner.RunAndWait(
					command, path, out output, out error);
				PNUnitServices.Get().WriteLine(output);

				if( result != 0 )
				{
					PNUnitServices.Get().WriteLine(
						string.Format("Command {0} failed with error code {1}",
						command, result));
				}

				return result;
			}
			catch( Exception e )
			{
				string errormsg = string.Format("Error executing command {0} on path {1}. Error = {2}", command, path, e.Message);
				PNUnitServices.Get().WriteLine(errormsg);
				Assert.Fail(errormsg);
				return 1;
			}
		}


	}
}
