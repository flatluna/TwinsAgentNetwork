using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TwinAgentsNetwork.Services
{
    public class AiTokrens
    {

        /// Based on: https://github.com/openai/openai-cookbook/blob/main/examples/How_to_count_tokens_with_tiktoken.ipynb
        /// </summary>
        /// <param name="messages">Messages to calculate token count for.</param>
        /// <returns>Number of tokens</returns>
        public int GetTokenCount(string Text)
        {
            const int TokensPerMessage = 3;
            const int TokensPerRole = 1;
            const int BaseTokens = 3;
            var disallowedSpecial = new HashSet<string>();

            var tokenCount = BaseTokens;

            var encoding = SharpToken.GptEncoding.GetEncoding("cl100k_base");
            tokenCount += encoding.Encode(Text, disallowedSpecial).Count;

            return tokenCount;
        }
    }
}
