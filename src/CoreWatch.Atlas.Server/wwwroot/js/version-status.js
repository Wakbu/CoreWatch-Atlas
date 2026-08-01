// CoreWatch Atlas module: version-status.
(()=>{"use strict";
const operationViews=new Set(["reports","groups","maintenance","assets","access"]);
const titles={reports:"서버 보고서",groups:"서버 그룹",maintenance:"유지보수",assets:"자산 관리",access:"사용자/API"};
let status;
function view(){return location.hash.replace(/^#?\/?|\/$/g,"")||"dashboard"}
function syncNavigation(){const current=view();if(!operationViews.has(current))return;document.querySelectorAll(".nav").forEach(x=>x.classList.toggle("active",x.dataset.view===current));const title=document.querySelector("#title");if(title)title.textContent=titles[current]}
function renderStatus(){if(view()!=="dashboard"||!status)return;const head=document.querySelector("#content .section-head");if(!head||document.querySelector("#releaseStatus"))return;const update=status.updateAvailable?` <strong>Update available: v${status.latestVersion}</strong>`:status.latestVersion?` <span>Latest: v${status.latestVersion}</span>`:"";head.insertAdjacentHTML("beforeend",`<span class="release-status" id="releaseStatus">CoreWatch v${status.currentVersion}${update}</span>`)}
async function loadStatus(){const response=await fetch("/api/v1/version",{headers:{Accept:"application/json"}});if(!response.ok)return;status=await response.json();renderStatus()}
addEventListener("hashchange",()=>{syncNavigation();renderStatus()});document.addEventListener("atlas:render",()=>{syncNavigation();renderStatus()});addEventListener("DOMContentLoaded",()=>{syncNavigation();loadStatus().catch(()=>{})});
})();
