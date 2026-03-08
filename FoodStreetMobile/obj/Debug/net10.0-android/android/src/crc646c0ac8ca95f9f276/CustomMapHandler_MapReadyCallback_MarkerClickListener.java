package crc646c0ac8ca95f9f276;


public class CustomMapHandler_MapReadyCallback_MarkerClickListener
	extends java.lang.Object
	implements
		mono.android.IGCUserPeer,
		com.google.android.gms.maps.GoogleMap.OnMarkerClickListener
{
/** @hide */
	public static final String __md_methods;
	static {
		__md_methods = 
			"n_onMarkerClick:(Lcom/google/android/gms/maps/model/Marker;)Z:GetOnMarkerClick_Lcom_google_android_gms_maps_model_Marker_Handler:Android.Gms.Maps.GoogleMap+IOnMarkerClickListenerInvoker, Xamarin.GooglePlayServices.Maps\n" +
			"";
		mono.android.Runtime.register ("CustomMapHandler+MapReadyCallback+MarkerClickListener, FoodStreetMobile", CustomMapHandler_MapReadyCallback_MarkerClickListener.class, __md_methods);
	}

	public CustomMapHandler_MapReadyCallback_MarkerClickListener ()
	{
		super ();
		if (getClass () == CustomMapHandler_MapReadyCallback_MarkerClickListener.class) {
			mono.android.TypeManager.Activate ("CustomMapHandler+MapReadyCallback+MarkerClickListener, FoodStreetMobile", "", this, new java.lang.Object[] {  });
		}
	}

	public boolean onMarkerClick (com.google.android.gms.maps.model.Marker p0)
	{
		return n_onMarkerClick (p0);
	}

	private native boolean n_onMarkerClick (com.google.android.gms.maps.model.Marker p0);

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
