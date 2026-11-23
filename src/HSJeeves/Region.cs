namespace HSJeeves
{
	// Maps the account id's high word to the region labels the backend expects.
	// Rule copied from HDT (Helper.GetRegion): (hi >> 32) & 0xFF, 1=US, 2=EU, 3=ASIA (sent as "AP").
	public static class Region
	{
		public static string FromAccountHi(ulong accountHi)
		{
			switch((accountHi >> 32) & 0xFF)
			{
				case 1: return "US";
				case 2: return "EU";
				case 3: return "AP";
				case 5: return "CHINA";
				default: return "UNKNOWN";
			}
		}
	}
}
