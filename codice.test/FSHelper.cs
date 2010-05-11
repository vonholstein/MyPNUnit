using System;
using System.IO;
using System.Security.Cryptography;

namespace Codice.Test
{
	public class FSHelper
	{
		public static void WriteFile(string name, string content)
		{
			StreamWriter writer = File.CreateText(name);
			writer.Write(content);
			writer.Flush();
			writer.Close();
		}

		public static void WriteFile(string name, byte[] data)
		{
			FileStream writer = new FileStream(
				name, 
				System.IO.FileMode.Create, 
				System.IO.FileAccess.Write, 
				System.IO.FileShare.None);

			if( data != null )
				writer.Write(data, 0, data.Length);
			writer.Close();
		}
        
		public static void AppendToFile(string name, string content)
		{
			StreamWriter writer = File.AppendText(name);
			writer.Write(content);
			writer.Flush();
			writer.Close();
		}

		public static string ReadFile(string name)
		{
			StreamReader reader = File.OpenText(name);
			string result = reader.ReadToEnd();            
			reader.Close();
			return result;
		}

		public static void DeleteDirectory(string path)
		{
			try
			{
				// fix! move the command to the root dir
				CmdRunner.ExecuteCommand("cm version", Path.GetPathRoot(path));
				Directory.Delete(path, true);
			}
			catch( UnauthorizedAccessException  )
			{
				// there are readonly files... try again                
				DeleteDirectoryRecurse(path);
			}
		}

		public static void DeleteCommonDirectory(string path)
		{
			try
			{
				try
				{
					Directory.Delete(path, true);
				}
				catch( UnauthorizedAccessException  )
				{

					if (!Directory.Exists(path))
						return;
					// there are readonly files... try again                
					DeleteDirectoryRecurse(path);
				}
			}
			catch( Exception ) 
			{
				//cocurrence with the tests try delete the same workspace in load tests
			}
		}

		private static void DeleteDirectoryRecurse(string path)
		{
			string[] files = Directory.GetFiles(path, "*");
			foreach( string file in files )
			{
				DeleteFile(file);
			}
			string[] dirs = Directory.GetDirectories(path, "*");
			foreach( string dir in dirs )
			{
				DeleteDirectoryRecurse(dir);
			}
			File.SetAttributes(path, FileAttributes.Normal);
			Directory.Delete(path);
		}

		public static void DeleteDirectoryChilds(string path)
		{
			string[] files = Directory.GetFiles(path, "*");
			foreach( string file in files )
			{
				DeleteFile(file);
			}
			string[] dirs = Directory.GetDirectories(path, "*");
			foreach( string dir in dirs )
			{
				DeleteDirectoryRecurse(dir);
			}
		}

		public static void DeleteFile(string file)
		{
			File.SetAttributes(file, FileAttributes.Normal);
			File.Delete(file);
		}

		public static void DeleteFiles(string path, string pattern)
		{
			string[] files = Directory.GetFiles(path, pattern);
			foreach( string file in files )
				DeleteFile(file);
		}

		public static void DeleteFileWaitingToAccess(string file)
		{
			int maxRetries = 100;
			int retries = 0;
			bool bSuccess = false;
			while (!bSuccess && retries < maxRetries)
			{
				try
				{
					DeleteFile(file);
					bSuccess = true;
				}
				catch (IOException )
				{
					retries++;
				}
			}
		}
		public static void DeleteFilesWaitingToAccess(string path, string pattern)
		{
			string[] files = Directory.GetFiles(path, pattern);
			foreach( string file in files ) 
			{
				DeleteFileWaitingToAccess(file);
			}

		}

		public static bool CompareFiles(string src, string dst)
		{
			byte[] srcbytes = ReadFileBytes(src);
			byte[] dstbytes = ReadFileBytes(dst);

			if( (srcbytes == null) && (dstbytes == null) )
				return true;

			if( (srcbytes == null) ^ (dstbytes == null) )
				return false;

			if( srcbytes.Length != dstbytes.Length )
				return false;

			for( int i = 0; i < srcbytes.Length; ++i )
				if( srcbytes[i] != dstbytes[i] )
					return false;

			return true;

		}

		public static byte[] ReadFileBytes(string name)
		{
			FileStream stream = new FileStream(
				name, 
				System.IO.FileMode.Open, 
				System.IO.FileAccess.Read, 
				System.IO.FileShare.ReadWrite);
            
			byte[] bytes = new byte[stream.Length];

			stream.Read(bytes, 0, (int)stream.Length);
            
			stream.Close();
			return bytes;
		}

		public static string FirstCharToLower(string path)
		{
			char[] buf = path.ToCharArray();
			buf[0] = char.ToLower(buf[0]);
			return new string(buf);
		}

		public static bool IsSameDirectory (string path1, string path2) 
		{
			path1 = FirstCharToLower(path1);
			path2 = FirstCharToLower(path2);
			if (path1.Length == path2.Length)
				return path1.Equals(path2);
			else if (path1.Length < path2.Length)
				return path2.Equals(path1+Path.DirectorySeparatorChar);
			else
				return path1.Equals(path2+Path.DirectorySeparatorChar);
		}

		public static string FormatPath(string path)
		{
			if( path.EndsWith(Path.DirectorySeparatorChar.ToString()) )
			{
				// remove the last directory separator
				path =  path.Substring(0, path.Length - 1);
			}
			switch (Environment.OSVersion.Platform)
			{
				case PlatformID.Win32Windows: 
				case PlatformID.Win32NT:
					if (path != null && path.Length > 0)
					{
						char[] buf = path.ToCharArray();
						buf[0] = char.ToLower(buf[0]);
						return new string(buf);                      
					}
					return path;
				default:
					return path;
			}
		}

		private static string RemoveLastSeparator(string path)
		{
			if( path.EndsWith(Path.DirectorySeparatorChar.ToString()) )
			{
				// remove the last directory separator
				return path.Substring(0, path.Length - 1);
			}
			return path;
		}

		public static string BuildFullPath(string itemPath, string itemName)
		{
			itemPath = RemoveLastSeparator(itemPath);

			if( itemName == string.Empty )
				return itemPath;

			return Path.Combine(itemPath, itemName);
		}

		private static string CalcHashCode(FileStream file)
		{
			MD5CryptoServiceProvider md5Provider = new MD5CryptoServiceProvider();
			Byte[] hash = md5Provider.ComputeHash(file);                         
			return Convert.ToBase64String(hash);
		}

		public static string CalcHashCode(string filename)
		{
			FileStream stream = new FileStream(
				filename, 
				System.IO.FileMode.Open, 
				System.IO.FileAccess.Read, 
				System.IO.FileShare.ReadWrite);

			string result = CalcHashCode(stream);

			stream.Close();

			return result;
		}

	}

}
