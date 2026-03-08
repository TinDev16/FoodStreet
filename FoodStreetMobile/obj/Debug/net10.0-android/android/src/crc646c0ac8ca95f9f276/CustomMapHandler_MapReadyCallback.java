package crc646c0ac8ca95f9f276;


public class CustomMapHandler_MapReadyCallback
	extends java.lang.Object
	implements
		mono.android.IGCUserPeer,
		com.google.android.gms.maps.OnMapReadyCallback
{
/** @hide */
	public static final String __md_methods;
	static {
		__md_methods = 
			"n_onMapReady:(Lcom/google/android/gms/maps/GoogleMap;)V:GetOnMapReady_Lcom_google_android_gms_maps_GoogleMap_Handler:Android.Gms.Maps.IOnMapReadyCallbackInvoker, Xamarin.GooglePlayServices.Maps\n" +
			"";
		mono.android.Runtime.register ("CustomMapHandler+MapReadyCallback, FoodStreetMobile", CustomMapHandler_MapReadyCallback.class, __md_methods);
	}

	public CustomMapHandler_MapReadyCallback ()
	{
		super ();
		if (getClass () == CustomMapHandler_MapReadyCallback.class) {
			mono.android.TypeManager.Activate ("CustomMapHandler+MapReadyCallback, FoodStreetMobile", "", this, new java.lang.Object[] {  });
		}
	}

	public void onMapReady (com.google.android.gms.maps.GoogleMap p0)
	{
		n_onMapReady (p0);
	}

	private native void n_onMapReady (com.google.android.gms.maps.GoogleMap p0);

	private java.util.ArrayList refList;
	public void monodroidAddReference (java.lang.Object obj)
	{
		if (refList == null)
			refList = new java.util.ArrayList ();
		refList.add (obj);
	}

	public void monodroidClearReferences ()
	{
		if (refList != null)
			refList.clear ();
	}
}
