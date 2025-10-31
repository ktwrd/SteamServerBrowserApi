/*
   Copyright 2025 Kate Ward <kate@dariox.club>

   Licensed under the Apache License, Version 2.0 (the "License");
   you may not use this file except in compliance with the License.
   You may obtain a copy of the License at

       http://www.apache.org/licenses/LICENSE-2.0

   Unless required by applicable law or agreed to in writing, software
   distributed under the License is distributed on an "AS IS" BASIS,
   WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
   See the License for the specific language governing permissions and
   limitations under the License.
*/

using GenHTTP.Engine.Internal;
using GenHTTP.Modules.Practices;

using SteamServerBrowserApi;

//
// URLs:
//   http://localhost:8080/swagger/
//   http://localhost:8080/openapi.json
//   http://localhost:8080/server/search
//   http://localhost:8080/server/info
//   http://localhost:8080/health
//

var project = new Project();
return await Host.Create()
                 .Handler(project.Setup())
                 .Defaults()
                 .Console()
#if DEBUG
                 .Development()
#endif
                 .RunAsync();
