(function(root){
  'use strict';
  const frame={width:240,height:416},windowPoints='120,30 216,138 120,364 24,138';
  function valid(point){return point&&['x','y'].every(axis=>typeof point[axis]==='number'&&Number.isFinite(point[axis])&&point[axis]>=0&&point[axis]<=1);}
  function normalize(point){return valid(point)?{x:point.x,y:point.y}:{x:.5,y:.5};}
  function layout(width,height,point){
    if(!(width>0&&height>0))throw Error('图片尺寸无效');
    const focus=normalize(point),x=Math.min(.99,Math.max(.01,focus.x)),y=Math.min(.99,Math.max(.01,focus.y));
    // Largest crop of the arrow's aspect ratio centered on the selected point.
    const scale=Math.max(frame.width/(width*2*Math.min(x,1-x)),frame.height/(height*2*Math.min(y,1-y)));
    const left=width*x-frame.width/scale/2,top=height*y-frame.height/scale/2;
    return {x:-left*scale,y:-top*scale,width:width*scale,height:height*scale};
  }
  const api={frame,windowPoints,valid,normalize,layout};
  if(typeof module==='object'&&module.exports)module.exports=api;else root.JunnaArrowFocus=api;
})(typeof globalThis==='object'?globalThis:this);
