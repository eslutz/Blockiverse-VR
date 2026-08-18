/*
 * Copyright (c) Meta Platforms, Inc. and affiliates.
 * All rights reserved.
 *
 * Licensed under the Oculus SDK License Agreement (the "License");
 * you may not use the Oculus SDK except in compliance with the License,
 * which is provided at the time of installation or download, or which
 * otherwise accompanies this software in either electronic or hard copy form.
 *
 * You may obtain a copy of the License at
 *
 * https://developer.oculus.com/licenses/oculussdk/
 *
 * Unless required by applicable law or agreed to in writing, the Oculus SDK
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */


using System;

namespace Meta.XR.MetaWand.Editor.API
{

    /// <summary>
    /// Represents a request to check the user's API usage limits.
    /// </summary>
    [Serializable]
    public class CheckUsage
    {
        public string usage_filter;
        public string access_token;
        public Error error;
    }

    /// <summary>
    /// Represents the response containing API usage limit information.
    /// </summary>
    [Serializable]
    public class CheckUsageResponse
    {
        public int mesh_preview_gen_usage_limit;
        public int mesh_full_gen_usage_limit;
        public int mesh_preview_gen_recent_usage_count;
        public int mesh_full_gen_recent_usage_count;
        public bool success;
        public string error_message;
        public Error error;
    }

    /// <summary>
    /// Represents a request to fetch a specific asset by its identifier.
    /// </summary>
    [Serializable]
    public class FetchAssetRequest
    {
        public string request_id;
        public string asset_id;
        public bool query_b64s = false;
        public SearchAssetsAttributes attributes;
        public string access_token;
        public AppInfoAttribute app_info = new AppInfoAttribute
        {
            name = Constants.CoreSDKPackageName,
            version = Utils.CoreSdkVersion.ToString(),
            build_channel = "release"
        };
    }

    /// <summary>
    /// Represents the response from fetching a specific asset.
    /// </summary>
    [Serializable]
    public class FetchAssetResponse
    {
        public bool success;
        public string asset_id;
        public string status;
        public string asset_type;
        public string asset_sub_type;
        public string asset_short_name;
        public string gen_model;
        public PreviewUrls preview_urls;
        public AssetPart[] asset_parts;
        public AssetMeta[] asset_metas;
        public string error_message;
        public Error error;
    }

    /// <summary>
    /// Contains metadata about an asset including polygon count information.
    /// </summary>
    [Serializable]
    public class AssetMeta
    {
        public int polycount;
        public int[] all_polycounts;
    }

    /// <summary>
    /// Contains URLs for asset preview images.
    /// </summary>
    [Serializable]
    public class PreviewUrls
    {
        public string image;
    }

    /// <summary>
    /// Contains download URLs for mesh assets in different formats.
    /// </summary>
    [Serializable]
    public class MeshUrls
    {
        public string glb;
        public string fbx;
    }

    /// <summary>
    /// Contains download URLs for different texture maps of an asset.
    /// </summary>
    [Serializable]
    public class TextureUrl
    {
        public string albedo;
        public string normal;
        public string roughness;
        public string metallic;
    }



    /// <summary>
    /// Represents a single part of a generated asset with its mesh, texture, and audio URLs.
    /// </summary>
    [Serializable]
    public class AssetPart
    {
        public MeshUrls mesh_urls;
        public TextureUrl[] texture_urls;
    }

    /// <summary>
    /// Represents a request to search for assets by text query.
    /// </summary>
    [Serializable]
    public class SearchAssetsRequest
    {
        public string request_id;
        public string search_text;
        public string top_k = "4";
        public SearchAssetsAttributes attributes;
        public string access_token;

        public AppInfoAttribute app_info = new AppInfoAttribute
        {
            name = Constants.CoreSDKPackageName,
            version = Utils.CoreSdkVersion.ToString(),
            build_channel = "release"
        };
    }

    /// <summary>
    /// Contains attribute filters for asset search requests.
    /// </summary>
    [Serializable]
    public class SearchAssetsAttributes
    {
        public MeshAttribute mesh;
    }

    /// <summary>
    /// Contains mesh-specific attributes for filtering search results.
    /// </summary>
    [Serializable]
    public class MeshAttribute
    {
        public int target_polycount;
    }

    /// <summary>
    /// Contains application information sent with API requests for tracking.
    /// </summary>
    [Serializable]
    public class AppInfoAttribute
    {
        public string name;
        public string version;
        public string version_code;
        public string build_channel;
    }

    /// <summary>
    /// Represents the response from an asset search request.
    /// </summary>
    [Serializable]
    public class SearchAssetsResponse
    {
        public bool success;
        public SearchAssetResult[] assets;
        public string error_message;
        public Error error;
    }

    /// <summary>
    /// Represents a single search result containing an asset and its relevance score.
    /// </summary>
    [Serializable]
    public class SearchAssetResult
    {
        public SearchAsset asset;
        public float similarity_score;
    }

    /// <summary>
    /// Represents a searched asset with its metadata, URLs, and generation details.
    /// </summary>
    [Serializable]
    public class SearchAsset
    {
        public string asset_id;
        public string status;
        public string asset_type;
        public string asset_sub_type;
        public string gen_model;
        public PreviewUrls preview_urls;
        public AssetPart[] asset_parts;
        public AssetMeta[] asset_metas;
        public bool success;
    }

    /// <summary>
    /// Represents an API error response with detailed error information.
    /// </summary>
    [Serializable]
    public class Error
    {
        public string message;
        public string type;
        public string code;
        public string error_subcode;
        public string error_user_title;
        public string error_user_msg;
    }

    /// <summary>
    /// Represents a request to log telemetry data such as user feedback.
    /// </summary>
    [Serializable]
    public class TelemetryRequest
    {
        public string action;
        public string request_id;
        public string asset_id;
        public string target_request_id;
        public string original_search_text;
        public string access_token;
        public AppInfoAttribute app_info = new AppInfoAttribute
        {
            name = Constants.CoreSDKPackageName,
            version = Utils.CoreSdkVersion.ToString(),
            build_channel = "release"
        };
    }

    /// <summary>
    /// Represents the response from a telemetry logging request.
    /// </summary>
    [Serializable]
    public class TelemetryResponse
    {
        public bool success;
        public string action;
        public string error_message;
        public Error error;
    }
}
