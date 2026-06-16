using System;

namespace Blockiverse.MetaPlatform
{
    public interface IUserAgeCategoryClient
    {
        void Get(Action<BlockiverseUserAgeCategoryState> completed);
    }
}
